using LocalIA.Core.Models;

namespace LocalIA.Core.Abstractions;

public interface IOllamaProcessManager : IEngineProcessManager
{
    Task<bool> EnsureRunningAsync(OllamaStartOptions options, CancellationToken ct = default);
}
