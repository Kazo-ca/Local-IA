using LocalIA.Core.Models;
using LocalIA.Infrastructure.Router;
using Microsoft.Extensions.Logging.Abstractions;

namespace LocalIA.Tests;

public class RouterModelIndexBuilderTests
{
    private static AppConfig NewConfig() => new()
    {
        OllamaServer = new OllamaServerSettings { Host = "127.0.0.1:11434" },
        LlamaCppServer = new LlamaCppServerSettings { DefaultHost = "127.0.0.1", DefaultPort = 8080 },
    };

    [Fact]
    public void Build_ResolvesOllamaTier_ToConfiguredGlobalHost()
    {
        var config = NewConfig();
        var profile = new ModelProfile { Name = "Profil" };
        profile.Tiers.Add(new ModelTier { Engine = EngineKind.Ollama, OllamaCustomModelName = "mon-modele", Label = "défaut" });
        config.Profiles.Add(profile);

        var index = RouterModelIndexBuilder.Build(config, NullLogger.Instance);

        var resolution = Assert.Single(index).Value;
        Assert.Equal(EngineKind.Ollama, resolution.Engine);
        Assert.Equal("127.0.0.1", resolution.TargetHost);
        Assert.Equal(11434, resolution.TargetPort);
    }

    [Fact]
    public void Build_ResolvesLlamaCppTier_ToPerTierPortOverride_WhenSet()
    {
        var config = NewConfig();
        var profile = new ModelProfile { Name = "Profil" };
        var tier = new ModelTier
        {
            Engine = EngineKind.LlamaCpp,
            Label = "max_accuracy",
            LlamaCppSource = new LlamaCppModelSource { LocalFilePath = @"E:\models\mon-modele.gguf" },
        };
        tier.Settings.ServerNetworking.Port = 9090;
        profile.Tiers.Add(tier);
        config.Profiles.Add(profile);

        var index = RouterModelIndexBuilder.Build(config, NullLogger.Instance);

        var resolution = index["mon-modele"];
        Assert.Equal(9090, resolution.TargetPort);
        Assert.Equal("127.0.0.1", resolution.TargetHost);
    }

    [Fact]
    public void Build_ResolvesLlamaCppTier_ToDefaultPort_WhenNoOverride()
    {
        var config = NewConfig();
        var profile = new ModelProfile { Name = "Profil" };
        profile.Tiers.Add(new ModelTier
        {
            Engine = EngineKind.LlamaCpp,
            Label = "max_accuracy",
            LlamaCppSource = new LlamaCppModelSource { LocalFilePath = @"E:\models\mon-modele.gguf" },
        });
        config.Profiles.Add(profile);

        var index = RouterModelIndexBuilder.Build(config, NullLogger.Instance);

        Assert.Equal(8080, index["mon-modele"].TargetPort);
    }

    [Fact]
    public void Build_SkipsOllamaTier_WithoutCustomModelName()
    {
        var config = NewConfig();
        var profile = new ModelProfile { Name = "Profil" };
        profile.Tiers.Add(new ModelTier { Engine = EngineKind.Ollama, OllamaBaseModel = "qwen2.5:7b", Label = "défaut" });
        config.Profiles.Add(profile);

        var index = RouterModelIndexBuilder.Build(config, NullLogger.Instance);

        Assert.Empty(index);
    }

    [Fact]
    public void Build_DuplicateLlamaCppId_KeepsTierWithLargestConfiguredContext()
    {
        var config = NewConfig();
        var profile = new ModelProfile { Name = "Profil" };
        var smallContext = new ModelTier
        {
            Engine = EngineKind.LlamaCpp,
            Label = "petit_contexte",
            LlamaCppSource = new LlamaCppModelSource { LocalFilePath = @"E:\models\mon-modele.gguf" },
        };
        smallContext.Settings.ContextMemory.ContextSize = 4096;
        var largeContext = new ModelTier
        {
            Engine = EngineKind.LlamaCpp,
            Label = "grand_contexte",
            LlamaCppSource = new LlamaCppModelSource { LocalFilePath = @"E:\models\mon-modele.gguf" },
        };
        largeContext.Settings.ContextMemory.ContextSize = 32768;
        profile.Tiers.Add(smallContext);
        profile.Tiers.Add(largeContext);
        config.Profiles.Add(profile);

        var index = RouterModelIndexBuilder.Build(config, NullLogger.Instance);

        var resolution = Assert.Single(index).Value;
        Assert.Equal("grand_contexte", resolution.Tier.Label);
    }
}
