using LocalIA.Core.Configuration;

namespace LocalIA.Tests;

public class SettingsBuildersTests
{
    [Fact]
    public void LlamaCppArgumentBuilder_OmitsEverything_WhenNothingSet()
    {
        var settings = new ModelTierSettings();

        var args = LlamaCppArgumentBuilder.Build(settings);

        // Seul -ngl est toujours émis (GpuLayers a une valeur par défaut non-nullable, Mode=Auto).
        Assert.Equal(["-ngl", "auto"], args);
    }

    [Fact]
    public void OllamaParameterBuilder_NeverEmitsAdapterKey_ApiCreateHasNoSuchParameter()
    {
        // Régression : /api/create d'Ollama attend un champ de premier niveau "adapters"
        // (dictionnaire nom -> digest SHA256 d'un blob déjà téléversé), pas une clé dans
        // "parameters" — un ancien mapping [EngineFlag(OllamaParameter = "adapter")] produisait
        // une clé silencieusement ignorée par Ollama, sans le moindre effet ni erreur.
        var settings = new ModelTierSettings();
        settings.Adapters.LoraPath = @"E:\loras\mon-adaptateur.gguf";

        var parameters = OllamaParameterBuilder.Build(settings);

        Assert.DoesNotContain("adapter", parameters.Keys);
    }

    [Fact]
    public void LlamaCppArgumentBuilder_EmitsScalarFlags()
    {
        var settings = new ModelTierSettings();
        settings.Sampling.Temperature = 0.2;
        settings.ContextMemory.ContextSize = 8192;

        var args = LlamaCppArgumentBuilder.Build(settings);

        Assert.Contains("--temp", args);
        Assert.Contains("0.2", args);
        Assert.Contains("--ctx-size", args);
        Assert.Contains("8192", args);
    }

    [Fact]
    public void LlamaCppArgumentBuilder_BareFlag_OnlyWhenBooleanTrue()
    {
        var settingsTrue = new ModelTierSettings();
        settingsTrue.ContextMemory.KvOffload = true;
        Assert.Contains("--kv-offload", LlamaCppArgumentBuilder.Build(settingsTrue));

        var settingsFalse = new ModelTierSettings();
        settingsFalse.ContextMemory.KvOffload = false;
        Assert.DoesNotContain("--kv-offload", LlamaCppArgumentBuilder.Build(settingsFalse));
    }

    [Fact]
    public void LlamaCppArgumentBuilder_EnumUsesExplicitWireFormat_NotLowercasedName()
    {
        var settings = new ModelTierSettings();
        settings.ContextMemory.CacheTypeK = CacheQuantType.Q8_0;
        settings.ContextMemory.LoadMode = MemoryLoadMode.MmapAndMlock;

        var args = LlamaCppArgumentBuilder.Build(settings);

        Assert.Contains("q8_0", args);
        Assert.Contains("mmap+mlock", args);
    }

    [Theory]
    [InlineData(GpuLayerMode.Auto, "auto")]
    [InlineData(GpuLayerMode.All, "all")]
    public void LlamaCppArgumentBuilder_GpuLayers_SentinelModes(GpuLayerMode mode, string expected)
    {
        var settings = new ModelTierSettings();
        settings.GpuOffload.GpuLayers = new GpuLayerSpec { Mode = mode };

        var args = LlamaCppArgumentBuilder.Build(settings);

        var index = args.IndexOf("-ngl");
        Assert.True(index >= 0);
        Assert.Equal(expected, args[index + 1]);
    }

    [Fact]
    public void LlamaCppArgumentBuilder_GpuLayers_ExplicitCount()
    {
        var settings = new ModelTierSettings();
        settings.GpuOffload.GpuLayers = new GpuLayerSpec { Mode = GpuLayerMode.Explicit, ExplicitCount = 24 };

        var args = LlamaCppArgumentBuilder.Build(settings);

        var index = args.IndexOf("-ngl");
        Assert.Equal("24", args[index + 1]);
    }

    [Fact]
    public void LlamaCppArgumentBuilder_StopSequences_OneFlagPerLine()
    {
        var settings = new ModelTierSettings();
        settings.Sampling.StopSequences = "### User\n### Assistant";

        var args = LlamaCppArgumentBuilder.Build(settings);

        var occurrences = args.Select((a, i) => (a, i)).Where(t => t.a == "--reverse-prompt").Select(t => t.i).ToList();
        Assert.Equal(2, occurrences.Count);
        Assert.Equal("### User", args[occurrences[0] + 1]);
        Assert.Equal("### Assistant", args[occurrences[1] + 1]);
    }

    [Fact]
    public void LlamaCppArgumentBuilder_AppendsExtraRawArgs_RespectingQuotes()
    {
        var settings = new ModelTierSettings();
        settings.Raw.ExtraLlamaCppArgs = "--alias \"my model\" --verbose";

        var args = LlamaCppArgumentBuilder.Build(settings);

        Assert.Contains("--alias", args);
        Assert.Contains("my model", args);
        Assert.Contains("--verbose", args);
    }

    [Fact]
    public void OllamaParameterBuilder_OmitsGpuLayers_WhenAuto()
    {
        var settings = new ModelTierSettings();

        var parameters = OllamaParameterBuilder.Build(settings);

        Assert.DoesNotContain("num_gpu", parameters.Keys);
    }

    [Fact]
    public void OllamaParameterBuilder_IncludesNumGpu_OnlyWhenExplicit()
    {
        var settings = new ModelTierSettings();
        settings.GpuOffload.GpuLayers = new GpuLayerSpec { Mode = GpuLayerMode.Explicit, ExplicitCount = 30 };

        var parameters = OllamaParameterBuilder.Build(settings);

        Assert.Equal(30, parameters["num_gpu"]);
    }

    [Fact]
    public void OllamaParameterBuilder_StopSequences_BecomesArray()
    {
        var settings = new ModelTierSettings();
        settings.Sampling.StopSequences = "### User\n### Assistant";

        var parameters = OllamaParameterBuilder.Build(settings);

        var stop = Assert.IsAssignableFrom<IEnumerable<string>>(parameters["stop"]);
        Assert.Equal(["### User", "### Assistant"], stop);
    }

    [Fact]
    public void OllamaParameterBuilder_MapsSharedConceptToOllamaKeyName()
    {
        var settings = new ModelTierSettings();
        settings.Sampling.MirostatEta = 0.15;

        var parameters = OllamaParameterBuilder.Build(settings);

        Assert.Equal(0.15, parameters["mirostat_eta"]);
    }

    [Fact]
    public void OllamaParameterBuilder_SkipsFieldsWithNoOllamaMapping()
    {
        var settings = new ModelTierSettings();
        settings.Sampling.PresencePenalty = 0.5; // llama.cpp uniquement, pas de OllamaParameter

        var parameters = OllamaParameterBuilder.Build(settings);

        Assert.DoesNotContain("presence_penalty", parameters.Keys);
    }

    [Fact]
    public void OllamaParameterBuilder_ParsesExtraParametersLines()
    {
        var settings = new ModelTierSettings();
        settings.Raw.ExtraOllamaParameters = "custom_key custom_value\nanother_key 42";

        var parameters = OllamaParameterBuilder.Build(settings);

        Assert.Equal("custom_value", parameters["custom_key"]);
        Assert.Equal("42", parameters["another_key"]);
    }
}
