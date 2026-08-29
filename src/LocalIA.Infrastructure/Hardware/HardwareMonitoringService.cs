using System.Management;
using CommunityToolkit.Mvvm.Messaging;
using LibreHardwareMonitor.Hardware;
using LocalIA.Core.Abstractions;
using LocalIA.Core.Messaging;
using LocalIA.Core.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LocalIA.Infrastructure.Hardware;

public sealed class HardwareMonitoringService : BackgroundService, IHardwareMonitorService
{
    private static readonly TimeSpan CpuRamInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan GpuInterval = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan EnginesInterval = TimeSpan.FromSeconds(2.5);
    private static readonly string[] MonitoredDriveLetters = ["C:\\", "E:\\"];

    private readonly NvidiaSmiReader _nvidiaSmiReader = new();
    private readonly Computer _computer = new() { IsCpuEnabled = true };
    private readonly IOllamaApiClient _ollamaApiClient;
    private readonly ILlamaCppApiClient _llamaCppApiClient;
    private readonly IOllamaProcessManager _ollamaProcessManager;
    private readonly ILlamaCppProcessManager _llamaCppProcessManager;
    private readonly IMessenger _messenger;
    private readonly ILogger<HardwareMonitoringService> _logger;
    private readonly object _lock = new();
    private HardwareSnapshot _current = new();

    public HardwareMonitoringService(
        IOllamaApiClient ollamaApiClient,
        ILlamaCppApiClient llamaCppApiClient,
        IOllamaProcessManager ollamaProcessManager,
        ILlamaCppProcessManager llamaCppProcessManager,
        IMessenger messenger,
        ILogger<HardwareMonitoringService> logger)
    {
        _ollamaApiClient = ollamaApiClient;
        _llamaCppApiClient = llamaCppApiClient;
        _ollamaProcessManager = ollamaProcessManager;
        _llamaCppProcessManager = llamaCppProcessManager;
        _messenger = messenger;
        _logger = logger;
    }

    public HardwareSnapshot Current
    {
        get
        {
            lock (_lock)
            {
                return _current;
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _computer.Open();
        }
        catch
        {
            // Capteurs potentiellement partiellement ouverts (ex. pilote WinRing0 bloqué par
            // Memory Integrity, droits insuffisants) : tenter de refermer avant de relancer, pour
            // ne pas laisser de capteurs matériels ouverts si le service échoue à démarrer.
            TryCloseComputer();
            throw;
        }

        try
        {
            await Task.WhenAll(
                RunLoopAsync(CpuRamInterval, UpdateCpuRamAsync, stoppingToken),
                RunLoopAsync(GpuInterval, UpdateGpuAsync, stoppingToken),
                RunLoopAsync(EnginesInterval, UpdateEnginesAsync, stoppingToken));
        }
        finally
        {
            TryCloseComputer();
        }
    }

    private void TryCloseComputer()
    {
        try
        {
            _computer.Close();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Échec de la fermeture des capteurs matériels");
        }
    }

    private async Task RunLoopAsync(TimeSpan interval, Func<CancellationToken, Task> updateAsync, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await updateAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Échec d'une itération de monitoring matériel");
            }
        }
        while (await timer.WaitForNextTickAsync(ct));
    }

    private Task UpdateCpuRamAsync(CancellationToken ct)
    {
        var (cpuName, cpuLoad) = ReadCpu();
        var (totalRam, usedRam) = ReadRamViaWmi();
        Publish(s => s with
        {
            CpuName = cpuName,
            CpuLoadPercent = cpuLoad,
            TotalRamBytes = totalRam,
            UsedRamBytes = usedRam,
        });
        return Task.CompletedTask;
    }

    private async Task UpdateGpuAsync(CancellationToken ct)
    {
        var stats = await _nvidiaSmiReader.ReadAsync(ct);
        if (stats is null)
        {
            return;
        }

        Publish(s => s with
        {
            GpuName = stats.Name,
            TotalVramBytes = stats.TotalMemoryMiB * 1024 * 1024,
            UsedVramBytes = stats.UsedMemoryMiB * 1024 * 1024,
            GpuLoadPercent = stats.UtilizationPercent,
            GpuTemperatureCelsius = stats.TemperatureCelsius,
        });
    }

    private async Task UpdateEnginesAsync(CancellationToken ct)
    {
        await _ollamaProcessManager.RefreshStatusAsync(ct);
        await _llamaCppProcessManager.RefreshStatusAsync(ct);

        var ollamaReachable = _ollamaProcessManager.Status == EngineStatus.Running;
        var llamaReachable = _llamaCppProcessManager.Status == EngineStatus.Running;

        var loadedModels = new List<LoadedModelInfo>();

        if (ollamaReachable)
        {
            var running = await _ollamaApiClient.ListRunningModelsAsync(ct);
            loadedModels.AddRange(running.Select(m => new LoadedModelInfo
            {
                Engine = EngineKind.Ollama,
                Name = m.Name,
                SizeBytes = m.SizeBytes,
                VramBytes = m.SizeVramBytes,
                ExpiresAt = m.ExpiresAt,
            }));
        }

        if (llamaReachable)
        {
            var models = await _llamaCppApiClient.ListModelsAsync(ct);
            loadedModels.AddRange(models.Select(m => new LoadedModelInfo
            {
                Engine = EngineKind.LlamaCpp,
                Name = m.Id,
                SizeBytes = m.SizeBytes ?? 0,
            }));
        }

        Publish(s => s with
        {
            OllamaStatus = _ollamaProcessManager.Status,
            OllamaProcessId = _ollamaProcessManager.ProcessId,
            LlamaCppStatus = _llamaCppProcessManager.Status,
            LlamaCppProcessId = _llamaCppProcessManager.ProcessId,
            LoadedModels = loadedModels,
            Disks = ReadDiskSpace(),
        });
    }

    private (string Name, double LoadPercent) ReadCpu()
    {
        foreach (var hardware in _computer.Hardware)
        {
            if (hardware.HardwareType != HardwareType.Cpu)
            {
                continue;
            }

            hardware.Update();
            var loadSensor =
                hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load && s.Name.Contains("Total", StringComparison.OrdinalIgnoreCase))
                ?? hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load);

            return (hardware.Name, loadSensor?.Value ?? 0);
        }

        return ("CPU inconnu", 0);
    }

    private static (long TotalBytes, long UsedBytes) ReadRamViaWmi()
    {
        using var searcher = new ManagementObjectSearcher(
            "SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");

        foreach (var result in searcher.Get())
        {
            var totalKb = Convert.ToInt64(result["TotalVisibleMemorySize"]);
            var freeKb = Convert.ToInt64(result["FreePhysicalMemory"]);
            return (totalKb * 1024, (totalKb - freeKb) * 1024);
        }

        return (0, 0);
    }

    private static IReadOnlyList<DiskSpaceInfo> ReadDiskSpace()
    {
        var disks = new List<DiskSpaceInfo>();
        foreach (var driveLetter in MonitoredDriveLetters)
        {
            var drive = new DriveInfo(driveLetter);
            if (drive.IsReady)
            {
                disks.Add(new DiskSpaceInfo(driveLetter, drive.AvailableFreeSpace, drive.TotalSize));
            }
        }

        return disks;
    }

    private void Publish(Func<HardwareSnapshot, HardwareSnapshot> update)
    {
        HardwareSnapshot updated;
        lock (_lock)
        {
            updated = update(_current) with { CapturedAt = DateTimeOffset.Now };
            _current = updated;
        }

        _messenger.Send(new HardwareSnapshotChangedMessage(updated));
    }

    public override void Dispose()
    {
        _computer.Close();
        base.Dispose();
    }
}
