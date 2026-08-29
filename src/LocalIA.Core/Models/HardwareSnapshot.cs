namespace LocalIA.Core.Models;

public sealed record DiskSpaceInfo(string DriveLetter, long FreeBytes, long TotalBytes);

public sealed record HardwareSnapshot
{
    public string CpuName { get; init; } = "";
    public double CpuLoadPercent { get; init; }

    public long TotalRamBytes { get; init; }
    public long UsedRamBytes { get; init; }
    public double RamLoadPercent => TotalRamBytes == 0 ? 0 : 100.0 * UsedRamBytes / TotalRamBytes;

    public string GpuName { get; init; } = "";
    public long TotalVramBytes { get; init; }
    public long UsedVramBytes { get; init; }
    public double VramLoadPercent => TotalVramBytes == 0 ? 0 : 100.0 * UsedVramBytes / TotalVramBytes;
    public double GpuLoadPercent { get; init; }
    public double GpuTemperatureCelsius { get; init; }

    public EngineStatus OllamaStatus { get; init; } = EngineStatus.Unknown;
    public int? OllamaProcessId { get; init; }
    public EngineStatus LlamaCppStatus { get; init; } = EngineStatus.Unknown;
    public int? LlamaCppProcessId { get; init; }

    public IReadOnlyList<LoadedModelInfo> LoadedModels { get; init; } = [];
    public IReadOnlyList<DiskSpaceInfo> Disks { get; init; } = [];

    public DateTimeOffset CapturedAt { get; init; }
}
