using LocalIA.Core.Abstractions;

namespace LocalIA.Infrastructure.Engines;

public sealed class EngineOrchestrationService(
    IOllamaApiClient ollamaApiClient,
    IOllamaProcessManager ollamaProcessManager,
    ILlamaCppProcessManager llamaCppProcessManager) : IEngineOrchestrationService
{
    public async Task UnloadAllModelsAsync(CancellationToken ct = default)
    {
        var runningModels = await ollamaApiClient.ListRunningModelsAsync(ct);
        foreach (var model in runningModels)
        {
            await ollamaApiClient.UnloadModelAsync(model.Name, ct);
        }

        await llamaCppProcessManager.StopAsync(ct);
    }

    public Task StopOllamaAsync(CancellationToken ct = default) => ollamaProcessManager.StopAsync(ct);

    public Task StopLlamaCppAsync(CancellationToken ct = default) => llamaCppProcessManager.StopAsync(ct);

    public async Task StopAllAsync(CancellationToken ct = default)
    {
        await StopOllamaAsync(ct);
        await StopLlamaCppAsync(ct);
    }
}
