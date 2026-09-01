using LocalIA.Core.Abstractions;
using LocalIA.Core.Models;
using Microsoft.Extensions.Logging;

namespace LocalIA.Infrastructure.Router;

/// <summary>
/// Arbitre unique de chargement/éviction, partagé par les deux moteurs (voir la section
/// "Arbitrage des ressources" du plan) :
/// - Ollama : plusieurs modèles peuvent cohabiter, mais seulement si la VRAM le permet réellement
///   (mesurée via <see cref="IHardwareMonitorService"/>, pas supposée).
/// - llama.cpp : contrainte dure supplémentaire (un seul processus/modèle à la fois) en plus de la
///   même vérification VRAM — les deux s'appliquent, la contrainte dure est vérifiée en premier
///   puisqu'elle ne dépend pas de la VRAM disponible.
/// Dans les deux cas, seuls les paliers que le routeur a lui-même chargés (présents dans
/// <see cref="IRouterConnectionTracker"/>) sont des candidats à l'éviction AUTOMATIQUE — un modèle
/// chargé manuellement (Dashboard, Chat/Test), ou occupé par une autre requête du routeur, ne
/// déclenche la boîte de dialogue de conflit (<see cref="IRouterConflictPrompter"/>) que si le
/// laisser passer sans y toucher.
/// </summary>
public sealed class RouterResourceArbiter(
    IRouterResourceEstimator estimator,
    IHardwareMonitorService hardwareMonitor,
    IRouterConnectionTracker tracker,
    IRouterModelResolver resolver,
    IOllamaProcessManager ollamaProcessManager,
    IOllamaApiClient ollamaApiClient,
    ILlamaCppProcessManager llamaCppProcessManager,
    ILlamaCppLaunchPlanner llamaCppLaunchPlanner,
    IRouterConflictPrompter conflictPrompter,
    IAppConfigRepository configRepository,
    ILogger<RouterResourceArbiter> logger)
    : IRouterResourceArbiter
{
    public async Task<RouterLoadResult> EnsureLoadedAsync(RouterModelResolution resolution, CancellationToken ct = default)
    {
        var config = await configRepository.LoadAsync(ct);
        return resolution.Engine == EngineKind.Ollama
            ? await EnsureOllamaLoadedAsync(resolution, config, ct)
            : await EnsureLlamaCppLoadedAsync(resolution, config, ct);
    }

    private async Task<RouterLoadResult> EnsureOllamaLoadedAsync(RouterModelResolution resolution, AppConfig config, CancellationToken ct)
    {
        var running = await ollamaApiClient.ListRunningModelsAsync(ct);
        var alreadyLoaded = running.Any(m => string.Equals(m.Name, resolution.ModelId, StringComparison.OrdinalIgnoreCase));

        var outcome = RouterLoadOutcome.Loaded;
        if (!alreadyLoaded)
        {
            var (roomOutcome, roomError) = await EnsureVramRoomAsync(resolution, config, ct);
            if (roomOutcome is RouterLoadOutcome.ConflictCancelled)
            {
                return new RouterLoadResult(RouterLoadOutcome.ConflictCancelled, roomError);
            }

            outcome = roomOutcome ?? RouterLoadOutcome.Loaded;
        }
        else
        {
            outcome = RouterLoadOutcome.AlreadyLoaded;
        }

        var started = await ollamaProcessManager.EnsureRunningAsync(new OllamaStartOptions
        {
            ModelsPath = config.Storage.OllamaModelsPath,
            Host = config.OllamaServer.Host,
            KeepAlive = config.OllamaServer.KeepAlive,
            NumParallel = config.OllamaServer.NumParallel,
        }, ct);

        if (!started)
        {
            return new RouterLoadResult(RouterLoadOutcome.Failed, "Échec du démarrage d'Ollama.");
        }

        return new RouterLoadResult(outcome);
    }

    private async Task<RouterLoadResult> EnsureLlamaCppLoadedAsync(RouterModelResolution resolution, AppConfig config, CancellationToken ct)
    {
        var expectedLabel = LlamaCppLaunchSettingsFactory.ComputeModelLabel(resolution.Tier);
        var currentLabel = llamaCppProcessManager.CurrentModelPath;
        var alreadyLoaded = expectedLabel is not null
            && string.Equals(expectedLabel, currentLabel, StringComparison.OrdinalIgnoreCase)
            && llamaCppProcessManager.Status == EngineStatus.Running;

        if (alreadyLoaded)
        {
            return new RouterLoadResult(RouterLoadOutcome.AlreadyLoaded);
        }

        var outcome = RouterLoadOutcome.Loaded;

        // Contrainte dure : un seul processus/modèle à la fois côté llama.cpp, indépendamment de
        // la VRAM disponible (LlamaCppProcessManager.StartAsync refuserait de toute façon).
        if (currentLabel is not null)
        {
            var (proceed, cancelResult) = await EnsureLlamaCppSlotFreeAsync(resolution, currentLabel, ct);
            if (!proceed)
            {
                return cancelResult!;
            }

            outcome = RouterLoadOutcome.EvictedIdleThenLoaded;
        }

        // Contention croisée avec Ollama sur le même GPU : même une fois llama.cpp libre, vérifier
        // qu'il reste assez de VRAM compte tenu de ce qu'Ollama a éventuellement chargé.
        var (roomOutcome, roomError) = await EnsureVramRoomAsync(resolution, config, ct);
        if (roomOutcome is RouterLoadOutcome.ConflictCancelled)
        {
            return new RouterLoadResult(RouterLoadOutcome.ConflictCancelled, roomError);
        }

        if (roomOutcome is { } resolvedOutcome)
        {
            outcome = resolvedOutcome;
        }

        LlamaCppLaunchSettings settings;
        try
        {
            settings = await llamaCppLaunchPlanner.BuildAsync(resolution.Tier, config, ct);
        }
        catch (InvalidOperationException ex)
        {
            return new RouterLoadResult(RouterLoadOutcome.Failed, ex.Message);
        }

        var started = await llamaCppProcessManager.StartAsync(settings, ct);
        if (!started)
        {
            return new RouterLoadResult(RouterLoadOutcome.Failed, "Échec du démarrage de llama-server.");
        }

        return new RouterLoadResult(outcome);
    }

    /// <summary>
    /// Gère la contrainte dure d'instance unique de llama.cpp : si le modèle actuellement chargé
    /// est idle et suivi par le routeur, il est déchargé automatiquement ; sinon (occupé, ou
    /// chargé hors routeur), l'utilisateur est sollicité via <see cref="IRouterConflictPrompter"/>.
    /// </summary>
    private async Task<(bool Proceed, RouterLoadResult? CancelResult)> EnsureLlamaCppSlotFreeAsync(
        RouterModelResolution target, string currentLabel, CancellationToken ct)
    {
        var index = await resolver.GetIndexAsync(ct);
        var currentTierId = LlamaCppTierLookup.FindLoadedTierId(currentLabel, index);

        if (currentTierId is { } tierId && tracker.TryBeginEviction(tierId, TimeSpan.Zero))
        {
            try
            {
                await llamaCppProcessManager.StopAsync(ct);
            }
            finally
            {
                tracker.EndEviction(tierId);
            }

            return (true, null);
        }

        var blockingLabel = currentTierId is { } knownTierId && index.Values.FirstOrDefault(r => r.Tier.Id == knownTierId) is { } known
            ? known.Tier.Label
            : "un modèle llama.cpp chargé hors du routeur";

        var choice = await conflictPrompter.PromptAsync(
            new RouterConflictContext(target.ModelId, target.Tier.Label, [blockingLabel]), ct);

        if (choice is RouterConflictResolution.CancelIncoming)
        {
            return (false, new RouterLoadResult(RouterLoadOutcome.ConflictCancelled, "Un autre modèle llama.cpp est en cours d'utilisation."));
        }

        await llamaCppProcessManager.StopAsync(ct);
        return (true, null);
    }

    /// <summary>
    /// Vérifie que le modèle cible tient dans la VRAM libre, en évinçant d'abord les candidats
    /// idle suivis par le routeur si besoin ; si ça ne suffit toujours pas, sollicite l'utilisateur
    /// pour les occupants restants (occupés, ou chargés hors routeur) via
    /// <see cref="IRouterConflictPrompter"/>. Retourne (null, null) si rien n'a dû être libéré
    /// (empreinte inconnue ou déjà suffisamment de VRAM libre).
    /// </summary>
    private async Task<(RouterLoadOutcome? Outcome, string? Error)> EnsureVramRoomAsync(RouterModelResolution target, AppConfig config, CancellationToken ct)
    {
        var footprint = await estimator.EstimateVramBytesAsync(target.Tier, ct);
        if (footprint is null)
        {
            return (null, null);
        }

        var snapshot = hardwareMonitor.Current;
        var freeVram = snapshot.TotalVramBytes - snapshot.UsedVramBytes;
        var needed = footprint.Value + config.Router.VramSafetyMarginBytes;
        if (freeVram >= needed)
        {
            return (null, null);
        }

        var index = await resolver.GetIndexAsync(ct);
        var candidates = await GetLoadedCandidatesAsync(target.Tier.Id, index, ct);
        candidates.Sort((a, b) =>
            (tracker.GetLastActivityAt(a.TierId) ?? DateTimeOffset.MinValue)
            .CompareTo(tracker.GetLastActivityAt(b.TierId) ?? DateTimeOffset.MinValue));

        var evictedAny = false;
        var blockers = new List<RouterModelResolution>();
        foreach (var (tierId, candidateResolution) in candidates)
        {
            if (freeVram >= needed)
            {
                break;
            }

            if (!tracker.TryBeginEviction(tierId, TimeSpan.Zero))
            {
                blockers.Add(candidateResolution);
                continue;
            }

            try
            {
                var freed = await EvictAsync(candidateResolution, ct);
                if (freed)
                {
                    var candidateFootprint = await estimator.EstimateVramBytesAsync(candidateResolution.Tier, ct) ?? 0;
                    freeVram += candidateFootprint;
                    evictedAny = true;
                    logger.LogInformation(
                        "Palier « {Profile}/{Tier} » déchargé automatiquement pour faire de la place au modèle « {TargetId} ».",
                        candidateResolution.Profile.Name, candidateResolution.Tier.Label, target.ModelId);
                }
            }
            finally
            {
                tracker.EndEviction(tierId);
            }
        }

        if (freeVram >= needed)
        {
            return (evictedAny ? RouterLoadOutcome.EvictedIdleThenLoaded : null, null);
        }

        var choice = await conflictPrompter.PromptAsync(
            new RouterConflictContext(target.ModelId, target.Tier.Label, blockers.Select(b => b.Tier.Label).ToList()), ct);

        if (choice is RouterConflictResolution.CancelIncoming)
        {
            return (RouterLoadOutcome.ConflictCancelled, "VRAM insuffisante et l'utilisateur a annulé la requête entrante.");
        }

        foreach (var blocker in blockers)
        {
            await EvictAsync(blocker, ct);
        }

        return (RouterLoadOutcome.ConflictDropped, null);
    }

    private Task<bool> EvictAsync(RouterModelResolution resolution, CancellationToken ct) =>
        resolution.Engine == EngineKind.Ollama
            ? ollamaApiClient.UnloadModelAsync(resolution.ModelId, ct)
            : llamaCppProcessManager.StopAsync(ct);

    private async Task<List<(Guid TierId, RouterModelResolution Resolution)>> GetLoadedCandidatesAsync(
        Guid excludeTierId, IReadOnlyDictionary<string, RouterModelResolution> index, CancellationToken ct)
    {
        var candidates = new List<(Guid, RouterModelResolution)>();

        var running = await ollamaApiClient.ListRunningModelsAsync(ct);
        foreach (var model in running)
        {
            if (index.TryGetValue(model.Name, out var resolution) && resolution.Tier.Id != excludeTierId)
            {
                candidates.Add((resolution.Tier.Id, resolution));
            }
        }

        var currentLlamaCppLabel = llamaCppProcessManager.CurrentModelPath;
        if (currentLlamaCppLabel is not null && LlamaCppTierLookup.FindLoadedTierId(currentLlamaCppLabel, index) is { } llamaTierId
            && llamaTierId != excludeTierId)
        {
            var llamaResolution = index.Values.FirstOrDefault(r => r.Tier.Id == llamaTierId);
            if (llamaResolution is not null)
            {
                candidates.Add((llamaTierId, llamaResolution));
            }
        }

        return candidates;
    }
}
