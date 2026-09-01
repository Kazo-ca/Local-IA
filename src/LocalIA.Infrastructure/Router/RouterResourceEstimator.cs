using LocalIA.Core.Abstractions;
using LocalIA.Core.Configuration;
using LocalIA.Core.Gguf;
using LocalIA.Core.Models;
using LocalIA.Core.MoeOffload;
using Microsoft.Extensions.Logging;

namespace LocalIA.Infrastructure.Router;

public sealed class RouterResourceEstimator(IOllamaApiClient ollamaApiClient, IGgufMetadataReader ggufReader, ILogger<RouterResourceEstimator> logger)
    : IRouterResourceEstimator
{
    // Marge forfaitaire pour les activations/buffers de calcul, qui ne sont pas dans la taille du
    // fichier sur disque — approximation volontairement simple pour une décision d'admission
    // rapide. Le cache KV, lui, est calculé via la formule partagée MoeVramCalculator.
    // EstimateKvCacheBytes plutôt qu'englobé dans ce facteur forfaitaire : sa taille dépend
    // fortement du contexte configuré (plusieurs Go de plus sur un contexte 32K-128K), un facteur
    // constant sous-estimerait alors l'empreinte réelle et laisserait l'arbitre admettre un modèle
    // que ce même calcul, ailleurs dans l'app, refuserait à juste titre.
    private const double LlamaCppActivationsOverheadFactor = 1.05;

    public async Task<long?> EstimateVramBytesAsync(ModelTier tier, CancellationToken ct = default)
    {
        return tier.Engine == EngineKind.Ollama
            ? await EstimateOllamaAsync(tier, ct)
            : EstimateLlamaCpp(tier);
    }

    private async Task<long?> EstimateOllamaAsync(ModelTier tier, CancellationToken ct)
    {
        if (tier.OllamaCustomModelName is not { Length: > 0 } modelName)
        {
            return null;
        }

        var running = await ollamaApiClient.ListRunningModelsAsync(ct);
        var runningMatch = running.FirstOrDefault(m => string.Equals(m.Name, modelName, StringComparison.OrdinalIgnoreCase));
        if (runningMatch is not null)
        {
            return runningMatch.SizeVramBytes;
        }

        var tags = await ollamaApiClient.ListTagsAsync(ct);
        var tagMatch = tags.FirstOrDefault(t => string.Equals(t.Name, modelName, StringComparison.OrdinalIgnoreCase));
        return tagMatch?.SizeBytes;
    }

    private long? EstimateLlamaCpp(ModelTier tier)
    {
        var path = tier.LlamaCppSource?.LocalFilePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var metadata = ggufReader.Read(path);
            var contextMemory = tier.Settings.ContextMemory;
            var kvCacheBytes = MoeVramCalculator.EstimateKvCacheBytes(
                metadata,
                contextMemory.ContextSize ?? ModelIdentifier.DefaultContextSize,
                contextMemory.CacheTypeK ?? CacheQuantType.F16,
                contextMemory.CacheTypeV ?? CacheQuantType.F16);

            return (long)(metadata.TotalFileSizeBytes * LlamaCppActivationsOverheadFactor) + kvCacheBytes;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Impossible d'estimer l'empreinte VRAM du palier « {Tier} » ({Path}).", tier.Label, path);
            return null;
        }
    }
}
