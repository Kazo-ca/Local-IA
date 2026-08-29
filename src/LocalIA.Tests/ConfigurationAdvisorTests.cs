using LocalIA.Core.Advisor;
using LocalIA.Core.Gguf;
using LocalIA.Core.Models;
using LocalIA.Core.MoeOffload;

namespace LocalIA.Tests;

public class ConfigurationAdvisorTests
{
    // Même modèle synthétique que MoeVramCalculatorTests : calqué sur les chiffres réels
    // mesurés sur Qwen3.6-35B-A3B (41 couches MoE, ~522 Mo d'experts/couche).
    private static GgufModelMetadata BuildMoeModel(int layerCount = 41)
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
            ContextLengthTrained = 131072,
            IsMoe = true,
            ExpertCount = 256,
            ExpertUsedCount = 8,
            Layers = layers,
            NonLayerTensorsSizeBytes = 703_000_000,
            TotalFileSizeBytes = 22_285_080_192,
        };
    }

    // Modèle dense synthétique (~7 Go, 32 couches) : pas de tenseurs _exps.
    private static GgufModelMetadata BuildDenseModel(int layerCount = 32)
    {
        var layers = Enumerable.Range(0, layerCount).Select(i => new GgufLayerInfo
        {
            Index = i,
            IsMoeLayer = false,
            AttentionSizeBytes = 90_000_000,
            DenseFfnSizeBytes = 110_000_000,
            MoeExpertsSizeBytes = 0,
        }).ToList();

        return new GgufModelMetadata
        {
            Architecture = "llama",
            QuantizationLabel = "Q4_K_M",
            BlockCount = layerCount,
            EmbeddingLength = 4096,
            AttentionHeadCount = 32,
            AttentionHeadCountKv = 8,
            ContextLengthTrained = 32768,
            IsMoe = false,
            Layers = layers,
            NonLayerTensorsSizeBytes = 300_000_000,
            TotalFileSizeBytes = 6_700_000_000,
        };
    }

    private static HardwareSnapshot BuildHardware(long totalVramBytes, long usedVramBytes = 0, long totalRamBytes = 64L * 1024 * 1024 * 1024)
        => new()
        {
            GpuName = "RTX 3060",
            TotalVramBytes = totalVramBytes,
            UsedVramBytes = usedVramBytes,
            TotalRamBytes = totalRamBytes,
            UsedRamBytes = 0,
        };

    private static ConfigurationAdvisor CreateAdvisor() => new(new MoeVramCalculator());

    [Fact]
    public void Evaluate_MoeModel_ComfortableFit_WhenVramIsGenerous()
    {
        var advisor = CreateAdvisor();
        var hardware = BuildHardware(totalVramBytes: 80L * 1024 * 1024 * 1024);
        var facts = new CandidateModelFacts { DisplayName = "test-moe", GgufMetadata = BuildMoeModel() };

        var result = advisor.Evaluate(hardware, facts, desiredContextSize: 4096);

        Assert.Equal(FitVerdict.ComfortableFit, result.Verdict);
        Assert.NotNull(result.MoeRecommendation);
        Assert.Equal(0, result.MoeRecommendation!.RecommendedNCpuMoe);
    }

    [Fact]
    public void Evaluate_MoeModel_TightFit_WhenHeadroomIsSmall()
    {
        var advisor = CreateAdvisor();
        // Assez de VRAM pour la partie fixe + KV + toutes les couches d'experts, plus la marge de
        // sécurité (1 Go) et un peu de mou (0.5 Go) — mais moins d'une deuxième marge complète, ce
        // qui doit tomber dans la zone "Serré" (marge <= headroom < 2x marge).
        var model = BuildMoeModel();
        var fixedBytes = model.NonLayerTensorsSizeBytes + model.Layers.Sum(l => l.AttentionSizeBytes + l.DenseFfnSizeBytes);
        var allExpertsBytes = model.Layers.Sum(l => l.MoeExpertsSizeBytes);
        var justEnough = fixedBytes + allExpertsBytes + 1_500_000_000L;
        var hardware = BuildHardware(totalVramBytes: justEnough);
        var facts = new CandidateModelFacts { DisplayName = "test-moe", GgufMetadata = model };

        var result = advisor.Evaluate(hardware, facts, desiredContextSize: 4096);

        Assert.Equal(FitVerdict.TightFit, result.Verdict);
    }

    [Fact]
    public void Evaluate_MoeModel_RamOnlyWillBeSlow_WhenEvenAllCpuOffloadDoesNotFitVram()
    {
        var advisor = CreateAdvisor();
        var hardware = BuildHardware(totalVramBytes: 1L * 1024 * 1024 * 1024, totalRamBytes: 64L * 1024 * 1024 * 1024);
        var model = BuildMoeModel();
        var facts = new CandidateModelFacts { DisplayName = "test-moe", GgufMetadata = model, FileSizeBytes = model.TotalFileSizeBytes };

        var result = advisor.Evaluate(hardware, facts, desiredContextSize: 4096);

        Assert.Equal(FitVerdict.RamOnlyWillBeSlow, result.Verdict);
    }

    [Fact]
    public void Evaluate_MoeModel_DoesNotFit_WhenFileLargerThanRamToo()
    {
        var advisor = CreateAdvisor();
        var hardware = BuildHardware(totalVramBytes: 1L * 1024 * 1024 * 1024, totalRamBytes: 4L * 1024 * 1024 * 1024);
        var model = BuildMoeModel();
        var facts = new CandidateModelFacts { DisplayName = "test-moe", GgufMetadata = model, FileSizeBytes = model.TotalFileSizeBytes };

        var result = advisor.Evaluate(hardware, facts, desiredContextSize: 4096);

        Assert.Equal(FitVerdict.DoesNotFit, result.Verdict);
    }

    [Fact]
    public void Evaluate_DenseModel_ComfortableFit_WhenAllLayersFitWithRoomToSpare()
    {
        var advisor = CreateAdvisor();
        var hardware = BuildHardware(totalVramBytes: 24L * 1024 * 1024 * 1024);
        var model = BuildDenseModel();
        var facts = new CandidateModelFacts { DisplayName = "test-dense", GgufMetadata = model };

        var result = advisor.Evaluate(hardware, facts, desiredContextSize: 4096);

        Assert.Equal(FitVerdict.ComfortableFit, result.Verdict);
        Assert.Equal(model.BlockCount, result.RecommendedGpuLayers);
    }

    [Fact]
    public void Evaluate_DenseModel_RamOnlyWillBeSlow_WhenVramTooSmallButRamIsEnough()
    {
        var advisor = CreateAdvisor();
        var hardware = BuildHardware(totalVramBytes: 1L * 1024 * 1024 * 1024, totalRamBytes: 64L * 1024 * 1024 * 1024);
        var model = BuildDenseModel();
        var facts = new CandidateModelFacts { DisplayName = "test-dense", GgufMetadata = model, FileSizeBytes = model.TotalFileSizeBytes };

        var result = advisor.Evaluate(hardware, facts, desiredContextSize: 4096);

        Assert.Equal(FitVerdict.RamOnlyWillBeSlow, result.Verdict);
        Assert.True(result.RecommendedGpuLayers < model.BlockCount);
    }

    [Fact]
    public void Evaluate_MissingGgufMetadata_FallsBackToFileSizeHeuristic()
    {
        var advisor = CreateAdvisor();
        var hardware = BuildHardware(totalVramBytes: 12L * 1024 * 1024 * 1024, totalRamBytes: 64L * 1024 * 1024 * 1024);
        var facts = new CandidateModelFacts { DisplayName = "not-downloaded-yet", GgufMetadata = null, FileSizeBytes = 6L * 1024 * 1024 * 1024 };

        var result = advisor.Evaluate(hardware, facts, desiredContextSize: 4096);

        Assert.Equal(FitVerdict.RamOnlyWillBeSlow, result.Verdict);
        Assert.Contains("indisponibles", result.Summary);
    }

    [Fact]
    public void Evaluate_NotesMentionAuxiliaryFiles_WhenDetected()
    {
        var advisor = CreateAdvisor();
        var hardware = BuildHardware(totalVramBytes: 80L * 1024 * 1024 * 1024);
        var facts = new CandidateModelFacts
        {
            DisplayName = "test-moe",
            GgufMetadata = BuildMoeModel(),
            HasMultimodalProjector = true,
            HasDraftModel = true,
        };

        var result = advisor.Evaluate(hardware, facts, desiredContextSize: 4096);

        Assert.Contains(result.Notes, n => n.Contains("multimodal"));
        Assert.Contains(result.Notes, n => n.Contains("spéculatif"));
    }

    [Fact]
    public void Evaluate_DenseModel_DoesNotDoubleCountNonLayerTensorsInPerLayerBudget()
    {
        // Régression : `bytesPerLayer` incluait autrefois NonLayerTensorsSizeBytes (300 Mo) en plus
        // de `budget` qui le déduisait déjà séparément — exigeant silencieusement ~300 Mo de VRAM
        // en trop avant d'admettre que les 32 couches tiennent. Avec exactement 8.4 Go de VRAM, la
        // formule correcte doit placer les 32 couches en GPU ; l'ancienne formule n'en plaçait que 31.
        var advisor = CreateAdvisor();
        var hardware = BuildHardware(totalVramBytes: 8_400_000_000L);
        var model = BuildDenseModel();
        var facts = new CandidateModelFacts { DisplayName = "test-dense", GgufMetadata = model };

        var result = advisor.Evaluate(hardware, facts, desiredContextSize: 4096);

        Assert.Equal(model.BlockCount, result.RecommendedGpuLayers);
        Assert.NotEqual(FitVerdict.RamOnlyWillBeSlow, result.Verdict);
        Assert.NotEqual(FitVerdict.DoesNotFit, result.Verdict);
    }

    [Fact]
    public void Evaluate_DenseModel_DegenerateBlockCount_DoesNotClaimComfortableFit()
    {
        // Régression : quand l'architecture GGUF n'est pas reconnue, BlockCount peut retomber à 0.
        // `gpuLayers = 0` et `fits = (0 >= 0) = true` faisaient alors passer "aucune information
        // exploitable" pour "tient confortablement en VRAM".
        var advisor = CreateAdvisor();
        var hardware = BuildHardware(totalVramBytes: 2L * 1024 * 1024 * 1024, totalRamBytes: 64L * 1024 * 1024 * 1024);
        var baseModel = BuildDenseModel();
        var model = new GgufModelMetadata
        {
            Architecture = "unknown",
            QuantizationLabel = baseModel.QuantizationLabel,
            BlockCount = 0,
            EmbeddingLength = baseModel.EmbeddingLength,
            AttentionHeadCount = baseModel.AttentionHeadCount,
            AttentionHeadCountKv = baseModel.AttentionHeadCountKv,
            ContextLengthTrained = baseModel.ContextLengthTrained,
            IsMoe = false,
            Layers = [],
            NonLayerTensorsSizeBytes = baseModel.NonLayerTensorsSizeBytes,
            TotalFileSizeBytes = baseModel.TotalFileSizeBytes,
        };
        var facts = new CandidateModelFacts { DisplayName = "test-dense-degenerate", GgufMetadata = model, FileSizeBytes = 6_700_000_000L };

        var result = advisor.Evaluate(hardware, facts, desiredContextSize: 4096);

        Assert.NotEqual(FitVerdict.ComfortableFit, result.Verdict);
    }

    [Fact]
    public void Evaluate_RecommendedContextSize_NeverExceedsTrainedContext()
    {
        var advisor = CreateAdvisor();
        var hardware = BuildHardware(totalVramBytes: 200L * 1024 * 1024 * 1024);
        var model = BuildDenseModel();
        var facts = new CandidateModelFacts { DisplayName = "test-dense", GgufMetadata = model };

        var result = advisor.Evaluate(hardware, facts, desiredContextSize: 999_999);

        Assert.True(result.RecommendedContextSize <= model.ContextLengthTrained);
    }
}
