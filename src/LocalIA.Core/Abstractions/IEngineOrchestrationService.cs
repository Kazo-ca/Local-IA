namespace LocalIA.Core.Abstractions;

public interface IEngineOrchestrationService
{
    Task UnloadAllModelsAsync(CancellationToken ct = default);
    Task StopOllamaAsync(CancellationToken ct = default);
    Task StopLlamaCppAsync(CancellationToken ct = default);
    Task StopAllAsync(CancellationToken ct = default);
}
