using LocalIA.Core.Configuration;
using LocalIA.Core.Models;

namespace LocalIA.Tests;

public class LlamaCppLaunchSettingsTests
{
    // FromTier vérifie désormais que LocalFilePath existe réellement sur disque (voir
    // ModelConfigurationViewModel : un chemin configuré mais introuvable doit être détecté ici,
    // pas planter llama-server.exe silencieusement) — un fichier réel, même vide, est donc
    // nécessaire pour les tests qui passent par cette branche.
    private sealed class TempGgufFile : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"local-ia-test-{Guid.NewGuid():N}.gguf");

        public TempGgufFile() => File.WriteAllBytes(Path, []);

        public void Dispose() => File.Delete(Path);
    }

    private static ModelTier BuildTier(LlamaCppModelSource? source) => new()
    {
        Label = "test-tier",
        Engine = EngineKind.LlamaCpp,
        LlamaCppSource = source,
    };

    [Fact]
    public void FromTier_LocalFile_EmitsDashMFlag()
    {
        using var file = new TempGgufFile();
        var tier = BuildTier(new LlamaCppModelSource { LocalFilePath = file.Path });

        var settings = LlamaCppLaunchSettingsFactory.FromTier(tier, @"C:\bin\llama-server.exe", huggingFaceApiToken: null, totalMoeLayers: 0);

        Assert.Equal(@"C:\bin\llama-server.exe", settings.ExecutablePath);
        Assert.Equal(file.Path, settings.ModelPath);
        Assert.Equal(["-m", file.Path], settings.Arguments.Take(2));
    }

    [Fact]
    public void FromTier_LocalFileTakesPriorityOverHfRepo_WhenBothPresent()
    {
        using var file = new TempGgufFile();
        var tier = BuildTier(new LlamaCppModelSource { LocalFilePath = file.Path, HfRepoId = "org/repo" });

        var settings = LlamaCppLaunchSettingsFactory.FromTier(tier, @"C:\bin\llama-server.exe", huggingFaceApiToken: null, totalMoeLayers: 0);

        Assert.Contains("-m", settings.Arguments);
        Assert.DoesNotContain("-hf", settings.Arguments);
    }

    [Fact]
    public void FromTier_LocalFileConfiguredButMissing_Throws()
    {
        var tier = BuildTier(new LlamaCppModelSource { LocalFilePath = @"E:\llama_models\does-not-exist.gguf" });

        Assert.Throws<InvalidOperationException>(() =>
            LlamaCppLaunchSettingsFactory.FromTier(tier, @"C:\bin\llama-server.exe", huggingFaceApiToken: null, totalMoeLayers: 0));
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
        using var file = new TempGgufFile();
        var tier = BuildTier(new LlamaCppModelSource { LocalFilePath = file.Path });
        tier.Settings.Sampling.Temperature = 0.42;

        var settings = LlamaCppLaunchSettingsFactory.FromTier(tier, @"C:\bin\llama-server.exe", huggingFaceApiToken: null, totalMoeLayers: 0);

        Assert.Contains("--temp", settings.Arguments);
        Assert.Contains("0.42", settings.Arguments);
    }
}
