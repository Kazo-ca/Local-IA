using LocalIA.Core.Abstractions;
using LocalIA.Core.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LocalIA.Infrastructure.Router;

/// <summary>
/// Décharge automatiquement les paliers que le routeur a lui-même chargés (jamais un modèle
/// manuel) et qui sont inactifs depuis au moins `RouterSettings.IdleUnloadGraceSeconds` — no-op
/// tant que le routeur n'est pas démarré. Enregistré inconditionnellement (coût d'un tick à
/// vide négligeable), suit le patron `BackgroundService` + `PeriodicTimer` de
/// HardwareMonitoringService.
/// </summary>
public sealed class RouterIdleUnloadSweepHostedService(
    IRouterService routerService,
    IRouterModelResolver resolver,
    IRouterConnectionTracker tracker,
    IOllamaApiClient ollamaApiClient,
    ILlamaCppProcessManager llamaCppProcessManager,
    IAppConfigRepository configRepository,
    ILogger<RouterIdleUnloadSweepHostedService> logger)
    : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SweepInterval);
        do
        {
            try
            {
                if (routerService.Status is RouterStatus.Running)
                {
                    await SweepOnceAsync(stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Échec d'une itération du balayage de déchargement automatique du routeur.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SweepOnceAsync(CancellationToken ct)
    {
        var config = await configRepository.LoadAsync(ct);
        var grace = TimeSpan.FromSeconds(config.Router.IdleUnloadGraceSeconds);
        var index = await resolver.GetIndexAsync(ct);

        var currentLlamaCppLabel = llamaCppProcessManager.CurrentModelPath;
        if (currentLlamaCppLabel is not null && LlamaCppTierLookup.FindLoadedTierId(currentLlamaCppLabel, index) is { } llamaTierId
            && tracker.TryBeginEviction(llamaTierId, grace))
        {
            try
            {
                await llamaCppProcessManager.StopAsync(ct);
                logger.LogInformation("llama.cpp déchargé automatiquement après {Grace}s d'inactivité.", grace.TotalSeconds);
            }
            finally
            {
                tracker.EndEviction(llamaTierId);
            }
        }

        var running = await ollamaApiClient.ListRunningModelsAsync(ct);
        foreach (var model in running)
        {
            if (!index.TryGetValue(model.Name, out var resolution) || !tracker.TryBeginEviction(resolution.Tier.Id, grace))
            {
                continue;
            }

            try
            {
                await ollamaApiClient.UnloadModelAsync(model.Name, ct);
                logger.LogInformation("Modèle Ollama « {Model} » déchargé automatiquement après {Grace}s d'inactivité.", model.Name, grace.TotalSeconds);
            }
            finally
            {
                tracker.EndEviction(resolution.Tier.Id);
            }
        }
    }
}
