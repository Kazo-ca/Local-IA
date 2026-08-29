using System.ComponentModel;
using System.Diagnostics;
using LocalIA.Core.Abstractions;
using LocalIA.Core.Models;

namespace LocalIA.Infrastructure.Engines;

public sealed class LlamaCppProcessManager : ILlamaCppProcessManager, IDisposable
{
    private const string ProcessName = "llama-server";

    private readonly ILlamaCppApiClient _apiClient;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _ownedProcess;

    public LlamaCppProcessManager(ILlamaCppApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public EngineKind Kind => EngineKind.LlamaCpp;
    public EngineStatus Status { get; private set; } = EngineStatus.Unknown;
    public int? ProcessId { get; private set; }
    public bool IsOwnedProcess { get; private set; }
    public string? CurrentModelPath { get; private set; }

    public event EventHandler<EngineStatusChangedEventArgs>? StatusChanged;
    public event EventHandler<EngineLogLineEventArgs>? LogLineReceived;
    public event EventHandler<int>? ProcessExited;

    public async Task RefreshStatusAsync(CancellationToken ct = default)
    {
        // Prise non bloquante : si Start/Stop est en cours (déjà titulaire de _gate), on saute ce
        // cycle plutôt que de lire un état en cours de mutation — le prochain tick (2,5s après)
        // rattrapera l'état final une fois l'opération terminée. StartAsync appelle
        // RefreshStatusCoreAsync directement (sans passer par ici) puisqu'il détient déjà _gate :
        // un second WaitAsync, même non bloquant, ne pourrait jamais réussir dans ce cas (le
        // sémaphore n'est pas réentrant) et sauterait à tort le rafraîchissement qu'il demande.
        if (!await _gate.WaitAsync(0, ct))
        {
            return;
        }

        try
        {
            await RefreshStatusCoreAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task RefreshStatusCoreAsync(CancellationToken ct)
    {
        if (_ownedProcess is { HasExited: false })
        {
            var stillReachable = await _apiClient.IsReachableAsync(ct);
            SetStatus(stillReachable ? EngineStatus.Running : EngineStatus.Starting, _ownedProcess.Id, isOwned: true);
            return;
        }

        var externalId = ProcessLookup.FindFirstIdByNamePrefix(ProcessName);
        if (externalId is null)
        {
            CurrentModelPath = null;
            SetStatus(EngineStatus.Stopped, null, isOwned: false);
            return;
        }

        var reachable = await _apiClient.IsReachableAsync(ct);
        if (reachable)
        {
            var models = await _apiClient.ListModelsAsync(ct);
            CurrentModelPath = models.FirstOrDefault()?.Id;
        }

        SetStatus(reachable ? EngineStatus.Running : EngineStatus.Starting, externalId, isOwned: false);
    }

    public async Task<bool> StartAsync(LlamaCppLaunchSettings settings, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var externalId = ProcessLookup.FindFirstIdByNamePrefix(ProcessName);
            if (externalId is not null || _ownedProcess is { HasExited: false })
            {
                // Un seul modèle à la fois côté llama.cpp : on ne remplace jamais une instance
                // active sans que l'appelant ait explicitement arrêté l'ancienne au préalable,
                // pour ne pas couper une génération en cours. Appelle le coeur directement (pas
                // RefreshStatusAsync) : _gate est déjà détenu par cette méthode, un second
                // WaitAsync échouerait toujours puisque le sémaphore n'est pas réentrant.
                await RefreshStatusCoreAsync(ct);
                return false;
            }

            SetStatus(EngineStatus.Starting, null, isOwned: false);

            var startInfo = new ProcessStartInfo(settings.ExecutablePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(settings.ExecutablePath),
            };
            foreach (var arg in settings.Arguments)
            {
                startInfo.ArgumentList.Add(arg);
            }

            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) => RaiseLog("stdout", e.Data);
            process.ErrorDataReceived += (_, e) => RaiseLog("stderr", e.Data);
            process.Exited += (_, _) =>
            {
                var exited = _ownedProcess;
                var exitedId = exited?.Id ?? 0;
                _ownedProcess = null;
                exited?.Dispose();
                IsOwnedProcess = false;
                CurrentModelPath = null;
                SetStatus(EngineStatus.Crashed, null, isOwned: false);
                ProcessExited?.Invoke(this, exitedId);
            };

            try
            {
                if (!process.Start())
                {
                    SetStatus(EngineStatus.Crashed, null, isOwned: false);
                    return false;
                }
            }
            catch (Win32Exception)
            {
                // ExecutablePath introuvable/invalide (mal configuré, fichier déplacé) — même
                // traitement que OllamaProcessManager.EnsureRunningAsync pour le même cas.
                SetStatus(EngineStatus.Crashed, null, isOwned: false);
                return false;
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _ownedProcess = process;

            var ready = await WaitForReadyAsync(TimeSpan.FromMinutes(3), ct);
            if (ready)
            {
                CurrentModelPath = settings.ModelPath;
            }

            SetStatus(ready ? EngineStatus.Running : EngineStatus.Crashed, process.Id, isOwned: true);
            return ready;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> StopAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            SetStatus(EngineStatus.Stopping, ProcessId, IsOwnedProcess);

            foreach (var pid in ProcessLookup.FindAllIdsByNamePrefix(ProcessName))
            {
                ProcessLookup.TryKill(pid);
            }

            var owned = _ownedProcess;
            _ownedProcess = null;
            owned?.Dispose();
            CurrentModelPath = null;
            SetStatus(EngineStatus.Stopped, null, isOwned: false);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool> WaitForReadyAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            while (!await _apiClient.IsReachableAsync(cts.Token))
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cts.Token);
            }

            return true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false;
        }
    }

    private void RaiseLog(string source, string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        LogLineReceived?.Invoke(this, new EngineLogLineEventArgs(source, text, DateTimeOffset.Now));
    }

    private void SetStatus(EngineStatus status, int? processId, bool isOwned)
    {
        Status = status;
        ProcessId = processId;
        IsOwnedProcess = isOwned;
        StatusChanged?.Invoke(this, new EngineStatusChangedEventArgs(status, processId, isOwned));
    }

    public void Dispose()
    {
        _ownedProcess?.Dispose();
        _gate.Dispose();
    }
}
