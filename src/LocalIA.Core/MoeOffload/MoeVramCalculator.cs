using System.Globalization;
using LocalIA.Core.Configuration;
using LocalIA.Core.Gguf;

namespace LocalIA.Core.MoeOffload;

public sealed class MoeVramCalculatorInput
{
    public required long AvailableVramBytes { get; init; }
    public long SafetyMarginBytes { get; init; } = 1024L * 1024 * 1024;
    public required GgufModelMetadata Model { get; init; }
    public int ContextSize { get; init; } = 4096;
    public CacheQuantType CacheTypeK { get; init; } = CacheQuantType.F16;
    public CacheQuantType CacheTypeV { get; init; } = CacheQuantType.F16;
}

public sealed class MoeVramRecommendation
{
    public required int RecommendedNCpuMoe { get; init; }
    public required int TotalMoeLayers { get; init; }
    public required long FixedResidentBytes { get; init; }
    public required long EstimatedKvCacheBytes { get; init; }
    public required long EstimatedVramUsageBytes { get; init; }
    public required long EstimatedHeadroomBytes { get; init; }
    public required bool FitsInBudget { get; init; }
    public required string Explanation { get; init; }
    public required IReadOnlyList<GgufLayerInfo> MoeLayersInOrder { get; init; }
}

public interface IMoeVramCalculator
{
    MoeVramRecommendation Recommend(MoeVramCalculatorInput input);
}

/// <summary>
/// Calcule combien de couches MoE peuvent rester en VRAM. Hypothèse (alignée avec la stratégie
/// documentée du dépôt) : la partie dense (attention + FFN dense + embeddings/tête) tient
/// entièrement en VRAM ; seul le nombre de couches d'experts MoE côté GPU est optimisé.
/// </summary>
public sealed class MoeVramCalculator : IMoeVramCalculator
{
    // Octets par élément du cache KV — approximation pour les types quantifiés par blocs
    // (le cache KV de llama.cpp n'utilise pas exactement le même format bloc que les poids).
    private static readonly Dictionary<CacheQuantType, double> CacheBytesPerElement = new()
    {
        [CacheQuantType.F32] = 4,
        [CacheQuantType.F16] = 2,
        [CacheQuantType.Bf16] = 2,
        [CacheQuantType.Q8_0] = 1.0625,
        [CacheQuantType.Q4_0] = 0.5625,
        [CacheQuantType.Q4_1] = 0.625,
        [CacheQuantType.Q5_0] = 0.6875,
        [CacheQuantType.Q5_1] = 0.75,
    };

    public MoeVramRecommendation Recommend(MoeVramCalculatorInput input)
    {
        var model = input.Model;
        var moeLayers = model.Layers.Where(l => l.IsMoeLayer).OrderBy(l => l.Index).ToList();

        var fixedResidentBytes = model.NonLayerTensorsSizeBytes
            + model.Layers.Sum(l => l.AttentionSizeBytes)
            + model.Layers.Sum(l => l.DenseFfnSizeBytes);

        var kvCacheBytes = EstimateKvCacheBytes(model, input.ContextSize, input.CacheTypeK, input.CacheTypeV);
        var budget = input.AvailableVramBytes - input.SafetyMarginBytes;

        var recommendedN = moeLayers.Count;
        var fits = false;
        for (var n = 0; n <= moeLayers.Count; n++)
        {
            var gpuResidentMoeBytes = moeLayers.Skip(n).Sum(l => l.MoeExpertsSizeBytes);
            var totalNeeded = fixedResidentBytes + kvCacheBytes + gpuResidentMoeBytes;
            if (totalNeeded <= budget)
            {
                recommendedN = n;
                fits = true;
                break;
            }
        }

        var finalGpuResidentMoeBytes = moeLayers.Skip(recommendedN).Sum(l => l.MoeExpertsSizeBytes);
        var estimatedUsage = fixedResidentBytes + kvCacheBytes + finalGpuResidentMoeBytes;

        return new MoeVramRecommendation
        {
            RecommendedNCpuMoe = recommendedN,
            TotalMoeLayers = moeLayers.Count,
            FixedResidentBytes = fixedResidentBytes,
            EstimatedKvCacheBytes = kvCacheBytes,
            EstimatedVramUsageBytes = estimatedUsage,
            EstimatedHeadroomBytes = input.AvailableVramBytes - estimatedUsage,
            FitsInBudget = fits,
            Explanation = BuildExplanation(input, fixedResidentBytes, kvCacheBytes, finalGpuResidentMoeBytes, recommendedN, moeLayers.Count, fits),
            MoeLayersInOrder = moeLayers,
        };
    }

    /// <summary>
    /// Estimation partagée avec <see cref="Advisor.ConfigurationAdvisor"/> — une seule formule pour
    /// les deux, afin qu'elles ne puissent pas diverger silencieusement l'une de l'autre.
    /// </summary>
    public static long EstimateKvCacheBytes(GgufModelMetadata model, int contextSize, CacheQuantType cacheTypeK, CacheQuantType cacheTypeV)
    {
        if (model.AttentionHeadCount <= 0 || model.EmbeddingLength <= 0 || model.BlockCount <= 0)
        {
            return 0;
        }

        // Préfère key_length/value_length quand les métadonnées GGUF les fournissent : sur
        // certaines architectures (ex. Gemma), le head_dim réel diffère de embedding/head_count.
        var fallbackHeadDim = model.EmbeddingLength / model.AttentionHeadCount;
        var keyDim = model.AttentionKeyLength > 0 ? model.AttentionKeyLength : fallbackHeadDim;
        var valueDim = model.AttentionValueLength > 0 ? model.AttentionValueLength : fallbackHeadDim;
        var headCountKv = model.AttentionHeadCountKv > 0 ? model.AttentionHeadCountKv : model.AttentionHeadCount;
        var bytesK = CacheBytesPerElement.GetValueOrDefault(cacheTypeK, 2);
        var bytesV = CacheBytesPerElement.GetValueOrDefault(cacheTypeV, 2);

        return (long)(model.BlockCount * (double)contextSize * headCountKv * (keyDim * bytesK + valueDim * bytesV));
    }

    private static string BuildExplanation(
        MoeVramCalculatorInput input, long fixedResidentBytes, long kvCacheBytes, long gpuMoeBytes,
        int recommendedN, int totalMoeLayers, bool fits)
    {
        double Go(long bytes) => bytes / 1024.0 / 1024.0 / 1024.0;
        var gpuMoeLayerCount = totalMoeLayers - recommendedN;
        var marginGo = Go(input.SafetyMarginBytes);
        var availableGo = Go(input.AvailableVramBytes);
        var totalGo = Go(fixedResidentBytes + kvCacheBytes + gpuMoeBytes);

        var line = string.Format(
            CultureInfo.InvariantCulture,
            "Partie fixe (attention + FFN dense + embeddings/tête) ≈ {0:0.00} Go + cache KV (contexte {1}) ≈ {2:0.00} Go + {3}/{4} couches d'experts MoE sur GPU ≈ {5:0.00} Go = {6:0.00} Go, pour {7:0.00} Go de VRAM disponible (marge de {8:0.00} Go déduite).",
            Go(fixedResidentBytes), input.ContextSize, Go(kvCacheBytes), gpuMoeLayerCount, totalMoeLayers, Go(gpuMoeBytes), totalGo, availableGo, marginGo);

        if (!fits)
        {
            line += $" Même en déchargeant les {totalMoeLayers} couches d'experts sur CPU, le modèle ne tient pas dans la VRAM disponible avec ce contexte — réduisez le contexte ou libérez de la VRAM.";
        }
        else if (recommendedN > 0)
        {
            line += $" Les {recommendedN} premières couches d'experts resteront en RAM CPU.";
        }
        else
        {
            line += " Toutes les couches d'experts tiennent en VRAM.";
        }

        return line;
    }
}
