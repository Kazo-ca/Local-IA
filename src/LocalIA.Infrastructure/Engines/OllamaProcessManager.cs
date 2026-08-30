using System.ComponentModel;
using System.Diagnostics;
using LocalIA.Core.Abstractions;
using LocalIA.Core.Models;
using Microsoft.Extensions.Logging;

namespace LocalIA.Infrastructure.Engines;

public sealed class OllamaProcessManager : IOllamaProcessManager, IDisposable
{
    private const string ProcessNamePrefix = "ollama";

    private readonly IOllamaApiClient _apiClient;
    private readonly ILogger<OllamaProcessManager> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _ownedProcess;
    private CancellationTokenSource? _logTailCts;

    public OllamaProcessManager(IOllamaApiClient apiClient, ILogger<OllamaProcessManager> logger)
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    public EngineKind Kind => EngineKind.Ollama;
    public EngineStatus Status { get; private set; } = EngineStatus.Unknown;
    public int? ProcessId { get; private set; }
    public bool IsOwnedProcess { get; private set; }

    public event EventHandler<EngineStatusChangedEventArgs>? StatusChanged;
    public event EventHandler<EngineLogLineEventArgs>? LogLineReceived;
    public event EventHandler<int>? ProcessExited;

    public async Task RefreshStatusAsync(CancellationToken ct = default)
    {
        // Prise non bloquante : si un Start/Stop est en cours (déjà titulaire de _gate), on saute
        // ce cycle plutôt que de lire un état en cours de mutation (_ownedProcess/IsOwnedProcess
        // pourraient être vus dans un état transitoire incohérent) — le prochain tick (2,5s après)
        // rattrapera l'état final une fois l'opération terminée.
        if (!await _gate.WaitAsync(0, ct))
        {
            return;
        }

        try
        {
            if (_ownedProcess is { HasExited: false })
            {
                var stillReachable = await _apiClient.IsReachableAsync(ct);
                SetStatus(stillReachable ? EngineStatus.Running : EngineStatus.Starting, _ownedProcess.Id, isOwned: true);
                return;
            }

            var externalId = ProcessLookup.FindFirstIdByNamePrefix(ProcessNamePrefix);
            if (externalId is null)
            {
                SetStatus(EngineStatus.Stopped, null, isOwned: false);
                return;
            }

            var reachable = await _apiClient.IsReachableAsync(ct);
            SetStatus(reachable ? EngineStatus.Running : EngineStatus.Starting, externalId, isOwned: false);
            StartLogTailIfNeeded();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> EnsureRunningAsync(OllamaStartOptions options, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            // Déjà notre propre process (ex. double-clic sur "Démarrer") : s'aligner sur l'état réel
            // au lieu de retomber sur la recherche externe, qui écraserait IsOwnedProcess à false et
            // déclencherait une seconde source de logs en doublon (StartLogTailIfNeeded).
            if (_ownedProcess is { HasExited: false } owned)
            {
                var stillReachable = await _apiClient.IsReachableAsync(ct);
                SetStatus(stillReachable ? EngineStatus.Running : EngineStatus.Starting, owned.Id, isOwned: true);
                return stillReachable;
            }

            var externalId = ProcessLookup.FindFirstIdByNamePrefix(ProcessNamePrefix);
            if (externalId is not null && await _apiClient.IsReachableAsync(ct))
            {
                SetStatus(EngineStatus.Running, externalId, isOwned: false);
                StartLogTailIfNeeded();
                return true;
            }

            SetStatus(EngineStatus.Starting, externalId, isOwned: false);

            // Un "ollama" tout juste installé n'est pas forcément déjà visible dans le PATH du
            // process courant (les variables d'environnement ne se propagent pas à un process déjà
            // lancé) — vérifier l'emplacement d'installation par défaut évite d'attendre un
            // redémarrage de l'app après un premier "Installer Ollama" réussi.
            var defaultInstallPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama", "ollama.exe");
            var executable = File.Exists(defaultInstallPath) ? defaultInstallPath : "ollama";

            var startInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                ArgumentList = { "serve" },
            };
            startInfo.Environment["OLLAMA_MODELS"] = options.ModelsPath;
            startInfo.Environment["OLLAMA_HOST"] = options.Host;
            startInfo.Environment["OLLAMA_KEEP_ALIVE"] = options.KeepAlive;
            startInfo.Environment["OLLAMA_NUM_PARALLEL"] = options.NumParallel.ToString();

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
            catch (Win32Exception ex)
            {
                // "ollama" introuvable (ni PATH, ni emplacement par défaut) — distinct de Crashed
                // (qui suppose qu'un exécutable a été trouvé et démarré) : NotInstalled permet à
                // l'UI de proposer un bouton d'installation plutôt qu'un message d'erreur muet.
                _logger.LogWarning(ex, "Impossible de démarrer Ollama : exécutable introuvable");
                SetStatus(EngineStatus.NotInstalled, null, isOwned: false);
                return false;
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _ownedProcess = process;

            var ready = await WaitForReadyAsync(TimeSpan.FromSeconds(20), ct);
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
            _logTailCts?.Cancel();
            _logTailCts?.Dispose();
            _logTailCts = null;

            foreach (var pid in ProcessLookup.FindAllIdsByNamePrefix(ProcessNamePrefix))
            {
                ProcessLookup.TryKill(pid);
            }

            var owned = _ownedProcess;
            _ownedProcess = null;
            owned?.Dispose();
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
                await Task.Delay(TimeSpan.FromSeconds(1), cts.Token);
            }

            return true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false;
        }
    }

    private void StartLogTailIfNeeded()
    {
        if (IsOwnedProcess || _logTailCts is not null)
        {
            return;
        }

        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ollama", "server.log");
        if (!File.Exists(logPath))
        {
            return;
        }

        _logTailCts = new CancellationTokenSource();
        _ = TailLogFileAsync(logPath, _logTailCts.Token);
    }

    private async Task TailLogFileAsync(string path, CancellationToken ct)
    {
        var offset = new FileInfo(path).Length;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        do
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (stream.Length < offset)
                {
                    offset = 0;
                }

                stream.Seek(offset, SeekOrigin.Begin);
                using var reader = new StreamReader(stream);
                string? line;
                while ((line = await reader.ReadLineAsync(ct)) is not null)
                {
                    RaiseLog("log-file", line);
                }

                offset = stream.Position;
            }
            catch (IOException ex)
            {
                _logger.LogDebug(ex, "Lecture du fichier de log Ollama momentanément indisponible");
            }
        }
        while (await timer.WaitForNextTickAsync(ct));
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
        _logTailCts?.Cancel();
        _logTailCts?.Dispose();
        _ownedProcess?.Dispose();
        _gate.Dispose();
    }
}
