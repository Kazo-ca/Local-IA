using LocalIA.Core.Models;

namespace LocalIA.Core.Abstractions;

public sealed class RouterStatusChangedEventArgs(RouterStatus status, int? port) : EventArgs
{
    public RouterStatus Status { get; } = status;
    public int? Port { get; } = port;
}

public interface IRouterService
{
    RouterStatus Status { get; }
    int? ActivePort { get; }

    event EventHandler<RouterStatusChangedEventArgs>? StatusChanged;

    Task<bool> StartAsync(RouterSettings settings, CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
}
