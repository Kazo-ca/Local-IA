using LocalIA.Core.Models;

namespace LocalIA.Core.Abstractions;

public sealed class EngineStatusChangedEventArgs(EngineStatus status, int? processId, bool isOwnedProcess) : EventArgs
{
    public EngineStatus Status { get; } = status;
    public int? ProcessId { get; } = processId;
    public bool IsOwnedProcess { get; } = isOwnedProcess;
}

public sealed class EngineLogLineEventArgs(string source, string text, DateTimeOffset timestamp) : EventArgs
{
    public string Source { get; } = source;
    public string Text { get; } = text;
    public DateTimeOffset Timestamp { get; } = timestamp;
}

public interface IEngineProcessManager
{
    EngineKind Kind { get; }
    EngineStatus Status { get; }
    int? ProcessId { get; }
    bool IsOwnedProcess { get; }

    event EventHandler<EngineStatusChangedEventArgs>? StatusChanged;
    event EventHandler<EngineLogLineEventArgs>? LogLineReceived;
    event EventHandler<int>? ProcessExited;

    Task RefreshStatusAsync(CancellationToken ct = default);
    Task<bool> StopAsync(CancellationToken ct = default);
}
