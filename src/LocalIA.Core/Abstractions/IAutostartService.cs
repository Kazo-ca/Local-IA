namespace LocalIA.Core.Abstractions;

public interface IAutostartService
{
    Task<bool> IsEnabledAsync(CancellationToken ct = default);
    Task EnableAsync(CancellationToken ct = default);
    Task DisableAsync(CancellationToken ct = default);
}
