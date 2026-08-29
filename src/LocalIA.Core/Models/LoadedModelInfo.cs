namespace LocalIA.Core.Models;

public sealed class LoadedModelInfo
{
    public required EngineKind Engine { get; init; }
    public required string Name { get; init; }
    public long SizeBytes { get; init; }
    public long VramBytes { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}
