using LocalIA.Core.Configuration;
using LocalIA.Core.Models;

namespace LocalIA.Tests;

public class LlamaCppLaunchSettingsTests
{
    private static ModelTier BuildTier(LlamaCppModelSource? source) => new()
    {
        Label = "test-tier",
        Engine = EngineKind.LlamaCpp,
        LlamaCppSource = source,
    };

    [Fact]
    public void FromTier_LocalFile_EmitsDashMFlag()
    {
        var tier = BuildTier(new LlamaCppModelSource { LocalFilePath = @"E:\llama_models\model.gguf" });

        var settings = LlamaCppLaunchSettingsFactory.FromTier(tier, @"C:\bin\llama-server.exe", huggingFaceApiToken: null, totalMoeLayers: 0);

        Assert.Equal(@"C:\bin\llama-server.exe", settings.ExecutablePath);
        Assert.Equal(@"E:\llama_models\model.gguf", settings.ModelPath);
        Assert.Equal(["-m", @"E:\llama_models\model.gguf"], settings.Arguments.Take(2));
    }

    [Fact]
    public void FromTier_LocalFileTakesPriorityOverHfRepo_WhenBothPresent()
    {
        var tier = BuildTier(new LlamaCppModelSource { LocalFilePath = @"E:\llama_models\model.gguf", HfRepoId = "org/repo" });

        var settings = LlamaCppLaunchSettingsFactory.FromTier(tier, @"C:\bin\llama-server.exe", huggingFaceApiToken: null, totalMoeLayers: 0);

        Assert.Contains("-m", settings.Arguments);
        Assert.DoesNotContain("-hf", settings.Arguments);
    }

    [Fact]
    public void FromTier_HfRepoWithoutLocalFile_EmitsDashHfWithQuant()
    {
        var tier = BuildTier(new LlamaCppModelSource { HfRepoId = "Qwen/Qwen2.5-Coder-32B-Instruct-GGUF", QuantHint = "Q4_K_M" });

        var settings = LlamaCppLaunchSettingsFactory.FromTier(tier, @"C:\bin\llama-server.exe", huggingFaceApiToken: null, totalMoeLayers: 0);

        var hfIndex = settings.Arguments.ToList().IndexOf("-hf");
        Assert.True(hfIndex >= 0);
        Assert.Equal("Qwen/Qwen2.5-Coder-32B-Instruct-GGUF:Q4_K_M", settings.Arguments[hfIndex + 1]);
        Assert.Equal("Qwen/Qwen2.5-Coder-32B-Instruct-GGUF:Q4_K_M", settings.ModelPath);
    }

    [Fact]
    public void FromTier_HfRepoWithFileAndToken_EmitsDashHffAndDashHft()
    {
        var tier = BuildTier(new LlamaCppModelSource { HfRepoId = "org/repo", HfFile = "model-Q4_K_M.gguf" });

        var settings = LlamaCppLaunchSettingsFactory.FromTier(tier, @"C:\bin\llama-server.exe", huggingFaceApiToken: "hf_secret", totalMoeLayers: 0);

        Assert.Contains("-hff", settings.Arguments);
        Assert.Contains("model-Q4_K_M.gguf", settings.Arguments);
        Assert.Contains("-hft", settings.Arguments);
        Assert.Contains("hf_secret", settings.Arguments);
    }

    [Fact]
    public void FromTier_NoSource_Throws()
    {
        var tier = BuildTier(source: null);

        Assert.Throws<InvalidOperationException>(() =>
            LlamaCppLaunchSettingsFactory.FromTier(tier, @"C:\bin\llama-server.exe", huggingFaceApiToken: null, totalMoeLayers: 0));
    }

    [Fact]
    public void FromTier_IncludesModelTierSettingsFlags_ViaSharedBuilder()
    {
        // Ne re-teste pas le détail de chaque flag (déjà couvert par SettingsBuildersTests) : vérifie
        // seulement que FromTier délègue bien à LlamaCppArgumentBuilder plutôt qu'à un second
        // générateur séparé qui risquerait de diverger.
        var tier = BuildTier(new LlamaCppModelSource { LocalFilePath = "model.gguf" });
        tier.Settings.Sampling.Temperature = 0.42;

        var settings = LlamaCppLaunchSettingsFactory.FromTier(tier, @"C:\bin\llama-server.exe", huggingFaceApiToken: null, totalMoeLayers: 0);

        Assert.Contains("--temp", settings.Arguments);
        Assert.Contains("0.42", settings.Arguments);
    }
}
