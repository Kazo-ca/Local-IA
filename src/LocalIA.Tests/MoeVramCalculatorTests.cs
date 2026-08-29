using LocalIA.Core.Configuration;
using LocalIA.Core.Gguf;
using LocalIA.Core.MoeOffload;

namespace LocalIA.Tests;

public class MoeVramCalculatorTests
{
    // Modèle synthétique calqué sur les chiffres réels mesurés sur Qwen3.6-35B-A3B
    // (qwen3.6-35b-a3b-q4_k_m.gguf) : 41 couches MoE, ~522 Mo d'experts par couche,
    // ~28 Mo d'attention et ~5 Mo de FFN dense par couche, ~0.70 Go hors-couches.
    private static GgufModelMetadata BuildQwenLikeModel(int layerCount = 41)
    {
        var layers = Enumerable.Range(0, layerCount).Select(i => new GgufLayerInfo
        {
            Index = i,
            IsMoeLayer = true,
            AttentionSizeBytes = 28_000_000,
            DenseFfnSizeBytes = 5_400_000,
            MoeExpertsSizeBytes = 522_000_000,
        }).ToList();

        return new GgufModelMetadata
        {
            Architecture = "qwen35moe",
            QuantizationLabel = "Q4_K",
            BlockCount = layerCount,
            EmbeddingLength = 2048,
            AttentionHeadCount = 16,
            AttentionHeadCountKv = 2,
            IsMoe = true,
            ExpertCount = 256,
            ExpertUsedCount = 8,
            Layers = layers,
            NonLayerTensorsSizeBytes = 703_000_000,
            TotalFileSizeBytes = 22_285_080_192,
        };
    }

    [Fact]
    public void Recommend_AllExpertsFitInVram_WhenBudgetIsGenerous()
    {
        var calculator = new MoeVramCalculator();
        var input = new MoeVramCalculatorInput
        {
            AvailableVramBytes = 80L * 1024 * 1024 * 1024, // 80 Go, largement suffisant
            Model = BuildQwenLikeModel(),
            ContextSize = 4096,
        };

        var result = calculator.Recommend(input);

        Assert.True(result.FitsInBudget);
        Assert.Equal(0, result.RecommendedNCpuMoe);
        Assert.Equal(41, result.TotalMoeLayers);
    }

    [Fact]
    public void Recommend_NoExpertsFit_WhenBudgetIsTiny()
    {
        var calculator = new MoeVramCalculator();
        var input = new MoeVramCalculatorInput
        {
            AvailableVramBytes = 2L * 1024 * 1024 * 1024, // 2 Go : à peine la partie fixe
            SafetyMarginBytes = 0,
            Model = BuildQwenLikeModel(),
            ContextSize = 2048,
        };

        var result = calculator.Recommend(input);

        Assert.Equal(41, result.RecommendedNCpuMoe);
    }

    [Fact]
    public void Recommend_RealisticRtx3060Budget_MatchesHandComputedRange()
    {
        // Reproduit approximativement le cas réel de l'utilisateur : RTX 3060 12 Go,
        // marge 1 Go, contexte 4096. Calcul à la main : fixe (~0.70+1.15+0.22 ≈ 2.07 Go)
        // + cache KV (41*4096*2*128*4 octets ≈ 0.17 Go) ≈ 2.24 Go de base ; il reste
        // ~8.76 Go pour les experts, soit ~8.76e9/522e6 ≈ 16-17 couches sur GPU.
        var calculator = new MoeVramCalculator();
        var input = new MoeVramCalculatorInput
        {
            AvailableVramBytes = 12L * 1024 * 1024 * 1024,
            SafetyMarginBytes = 1024L * 1024 * 1024,
            Model = BuildQwenLikeModel(),
            ContextSize = 4096,
        };

        var result = calculator.Recommend(input);

        Assert.True(result.FitsInBudget);
        // Entre 22 et 27 couches sur CPU (donc 14 à 19 sur GPU) : large tolérance car
        // l'estimation du cache KV est approximative, mais l'ordre de grandeur doit être bon.
        Assert.InRange(result.RecommendedNCpuMoe, 20, 29);
        Assert.True(result.EstimatedVramUsageBytes <= input.AvailableVramBytes - input.SafetyMarginBytes);
    }

    [Fact]
    public void Recommend_ChoosesSmallestNThatFits_MonotonicBoundary()
    {
        var calculator = new MoeVramCalculator();
        var model = BuildQwenLikeModel();
        var input = new MoeVramCalculatorInput
        {
            AvailableVramBytes = 12L * 1024 * 1024 * 1024,
            SafetyMarginBytes = 1024L * 1024 * 1024,
            Model = model,
            ContextSize = 4096,
        };

        var result = calculator.Recommend(input);

        // Un N plus petit que la recommandation ne doit PAS tenir dans le budget...
        if (result.RecommendedNCpuMoe > 0)
        {
            var gpuBytesWithOneLess = model.Layers.Skip(result.RecommendedNCpuMoe - 1).Sum(l => l.MoeExpertsSizeBytes);
            var neededWithOneLess = result.FixedResidentBytes + result.EstimatedKvCacheBytes + gpuBytesWithOneLess;
            Assert.True(neededWithOneLess > input.AvailableVramBytes - input.SafetyMarginBytes);
        }

        // ...et la recommandation elle-même doit tenir.
        Assert.True(result.EstimatedVramUsageBytes <= input.AvailableVramBytes - input.SafetyMarginBytes);
    }

    [Fact]
    public void Recommend_ExplanationContainsRealNumbers()
    {
        var calculator = new MoeVramCalculator();
        var input = new MoeVramCalculatorInput
        {
            AvailableVramBytes = 12L * 1024 * 1024 * 1024,
            Model = BuildQwenLikeModel(),
            ContextSize = 4096,
        };

        var result = calculator.Recommend(input);

        Assert.Contains("41", result.Explanation);
        Assert.Contains("Go", result.Explanation);
    }

    [Fact]
    public void MoeOffloadSettings_ContiguousPrefix_EmitsSimpleFlag()
    {
        var settings = new MoeOffloadSettings { CpuLayerIndices = [0, 1, 2, 3, 4] };

        var args = settings.BuildLlamaCppArgs(totalMoeLayers: 41);

        Assert.Equal(["--n-cpu-moe", "5"], args);
    }

    [Fact]
    public void MoeOffloadSettings_AllLayers_EmitsCpuMoeFlag()
    {
        var settings = new MoeOffloadSettings { CpuLayerIndices = [.. Enumerable.Range(0, 41)] };

        var args = settings.BuildLlamaCppArgs(totalMoeLayers: 41);

        Assert.Equal(["--cpu-moe"], args);
    }

    [Fact]
    public void MoeOffloadSettings_NoLayers_EmitsNothing()
    {
        var settings = new MoeOffloadSettings();

        var args = settings.BuildLlamaCppArgs(totalMoeLayers: 41);

        Assert.Empty(args);
    }

    [Fact]
    public void MoeOffloadSettings_NonContiguousSelection_EmitsDisjointOverrideTensorPatterns()
    {
        var settings = new MoeOffloadSettings { CpuLayerIndices = [0, 5, 10] };

        var args = settings.BuildLlamaCppArgs(totalMoeLayers: 41);

        Assert.Equal(4, args.Count);
        Assert.Equal("--override-tensor", args[0]);
        Assert.Contains("0|5|10", args[1]);
        Assert.Contains("=CPU", args[1]);
        Assert.Equal("--override-tensor", args[2]);
        Assert.Contains("=CUDA0", args[3]);

        var gpuIndices = ExtractIndices(args[3]);
        Assert.DoesNotContain(0, gpuIndices);
        Assert.DoesNotContain(5, gpuIndices);
        Assert.DoesNotContain(10, gpuIndices);
        Assert.Equal(38, gpuIndices.Count);
    }

    [Fact]
    public void Recommend_UsesAttentionKeyValueLength_WhenGgufProvidesThem_InsteadOfDividingEmbedding()
    {
        // Régression : le head_dim était toujours dérivé de EmbeddingLength/AttentionHeadCount,
        // même quand le GGUF fournit key_length/value_length explicites (nécessaire pour des
        // architectures comme Gemma, où le head_dim réel diffère de cette simple division).
        var withoutExplicitLength = BuildQwenLikeModel();
        var withExplicitLength = new GgufModelMetadata
        {
            Architecture = withoutExplicitLength.Architecture,
            QuantizationLabel = withoutExplicitLength.QuantizationLabel,
            BlockCount = withoutExplicitLength.BlockCount,
            EmbeddingLength = withoutExplicitLength.EmbeddingLength,
            AttentionHeadCount = withoutExplicitLength.AttentionHeadCount,
            AttentionHeadCountKv = withoutExplicitLength.AttentionHeadCountKv,
            AttentionKeyLength = 256, // délibérément différent de EmbeddingLength/AttentionHeadCount (128)
            AttentionValueLength = 256,
            IsMoe = true,
            ExpertCount = withoutExplicitLength.ExpertCount,
            ExpertUsedCount = withoutExplicitLength.ExpertUsedCount,
            Layers = withoutExplicitLength.Layers,
            NonLayerTensorsSizeBytes = withoutExplicitLength.NonLayerTensorsSizeBytes,
            TotalFileSizeBytes = withoutExplicitLength.TotalFileSizeBytes,
        };

        var kvWithout = MoeVramCalculator.EstimateKvCacheBytes(withoutExplicitLength, 4096, CacheQuantType.F16, CacheQuantType.F16);
        var kvWith = MoeVramCalculator.EstimateKvCacheBytes(withExplicitLength, 4096, CacheQuantType.F16, CacheQuantType.F16);

        Assert.Equal(kvWithout * 2, kvWith);
    }

    [Fact]
    public void Recommend_QuantizedCache_UsesLessVramThanF16()
    {
        // Régression : CacheTypeK/V configuré par l'utilisateur (ex. Q4_0 pour économiser de la
        // VRAM) n'atteignait jamais le calculateur — F16 était utilisé sans condition.
        var model = BuildQwenLikeModel();

        var f16 = MoeVramCalculator.EstimateKvCacheBytes(model, 4096, CacheQuantType.F16, CacheQuantType.F16);
        var q4 = MoeVramCalculator.EstimateKvCacheBytes(model, 4096, CacheQuantType.Q4_0, CacheQuantType.Q4_0);

        Assert.True(q4 < f16);
    }

    private static List<int> ExtractIndices(string overrideTensorPattern)
    {
        var start = overrideTensorPattern.IndexOf('(') + 1;
        var end = overrideTensorPattern.IndexOf(')');
        return overrideTensorPattern[start..end].Split('|').Select(int.Parse).ToList();
    }
}
