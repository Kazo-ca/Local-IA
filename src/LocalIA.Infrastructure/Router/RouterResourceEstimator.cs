using LocalIA.Core.Abstractions;
using LocalIA.Core.Gguf;
using LocalIA.Core.Models;
using Microsoft.Extensions.Logging;

namespace LocalIA.Infrastructure.Router;

public sealed class RouterResourceEstimator(IOllamaApiClient ollamaApiClient, IGgufMetadataReader ggufReader, ILogger<RouterResourceEstimator> logger)
    : IRouterResourceEstimator
{
    // Marge forfaitaire pour le cache KV/activations, qui ne sont pas dans la taille du fichier
    // sur disque — approximation volontairement simple pour une décision d'admission rapide (le
    // conseiller MoE calcule un placement précis par couche pour un besoin différent : configurer
    // le déchargement CPU/GPU d'un palier, pas arbitrer entre plusieurs modèles en mémoire).
    private const double LlamaCppKvCacheOverheadFactor = 1.15;

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
            return (long)(metadata.TotalFileSizeBytes * LlamaCppKvCacheOverheadFactor);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Impossible d'estimer l'empreinte VRAM du palier « {Tier} » ({Path}).", tier.Label, path);
            return null;
        }
    }
}
