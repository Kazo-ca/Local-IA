using System.Text.Json;
using LocalIA.Infrastructure.Configuration;

namespace LocalIA.Tests;

public class LegacyConfigImporterTests
{
    private sealed class TempRepo : IDisposable
    {
        public string RootPath { get; } = Path.Combine(Path.GetTempPath(), $"local-ia-legacy-test-{Guid.NewGuid():N}");

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }

    private static void WriteConfigJson(string repoRoot, string modelfileRelativePath)
    {
        Directory.CreateDirectory(Path.Combine(repoRoot, "config"));
        var json = $$"""
            {
              "storage": { "modelsPath": "E:\\ollama_models" },
              "profiles": {
                "test_profile": {
                  "name": "Profil de test",
                  "tiers": {
                    "max_accuracy": {
                      "base_model": "qwen2.5:14b",
                      "custom_model_name": "local-ia-test:14b",
                      "modelfile": "{{modelfileRelativePath.Replace("\\", "\\\\")}}",
                      "tool_calling": true
                    }
                  }
                }
              }
            }
            """;
        File.WriteAllText(Path.Combine(repoRoot, "config", "config.json"), json);
    }

    [Fact]
    public async Task TryImportAsync_ReturnsNull_WhenLegacyConfigDoesNotExist()
    {
        using var repo = new TempRepo();
        Directory.CreateDirectory(repo.RootPath);
        var importer = new LegacyConfigImporter();

        var result = await importer.TryImportAsync(repo.RootPath);

        Assert.Null(result);
    }

    [Fact]
    public async Task TryImportAsync_ParsesKnownParametersAndSystemPrompt()
    {
        using var repo = new TempRepo();
        Directory.CreateDirectory(Path.Combine(repo.RootPath, "config", "Modelfiles"));
        WriteConfigJson(repo.RootPath, "config/Modelfiles/Modelfile.test");
        await File.WriteAllTextAsync(Path.Combine(repo.RootPath, "config", "Modelfiles", "Modelfile.test"), """"
            FROM qwen2.5:14b
            SYSTEM """Tu es un assistant de test."""
            PARAMETER temperature 0.15
            PARAMETER top_p 0.92
            PARAMETER repeat_penalty 1.08
            PARAMETER num_ctx 16384
            """");

        var importer = new LegacyConfigImporter();
        var result = await importer.TryImportAsync(repo.RootPath);

        var tier = Assert.Single(Assert.Single(result!.Profiles).Tiers);
        Assert.Equal("Tu es un assistant de test.", tier.SystemPrompt);
        Assert.Equal(0.15, tier.Settings.Sampling.Temperature);
        Assert.Equal(0.92, tier.Settings.Sampling.TopP);
        Assert.Equal(1.08, tier.Settings.Sampling.RepeatPenalty);
        Assert.Equal(16384, tier.Settings.ContextMemory.ContextSize);
    }

    [Fact]
    public async Task TryImportAsync_RecognizesParametersBeyondTheOriginalFour()
    {
        // Régression : ParseModelfile ne reconnaissait autrefois que 4 clés PARAMETER codées en
        // dur ; toute autre clé (pourtant déjà modélisée ailleurs dans l'app, via [EngineFlag])
        // était silencieusement perdue à l'import. "seed" est mappé sur Sampling.Seed.
        using var repo = new TempRepo();
        Directory.CreateDirectory(Path.Combine(repo.RootPath, "config", "Modelfiles"));
        WriteConfigJson(repo.RootPath, "config/Modelfiles/Modelfile.test");
        await File.WriteAllTextAsync(Path.Combine(repo.RootPath, "config", "Modelfiles", "Modelfile.test"), """
            FROM qwen2.5:14b
            PARAMETER seed 42
            """);

        var importer = new LegacyConfigImporter();
        var result = await importer.TryImportAsync(repo.RootPath);

        var tier = Assert.Single(Assert.Single(result!.Profiles).Tiers);
        Assert.Equal(42, tier.Settings.Sampling.Seed);
    }

    [Fact]
    public async Task TryImportAsync_UnrecognizedParameter_IsIgnoredWithoutThrowing()
    {
        using var repo = new TempRepo();
        Directory.CreateDirectory(Path.Combine(repo.RootPath, "config", "Modelfiles"));
        WriteConfigJson(repo.RootPath, "config/Modelfiles/Modelfile.test");
        await File.WriteAllTextAsync(Path.Combine(repo.RootPath, "config", "Modelfiles", "Modelfile.test"), """
            FROM qwen2.5:14b
            PARAMETER some_future_unknown_flag abc
            """);

        var importer = new LegacyConfigImporter();
        var result = await importer.TryImportAsync(repo.RootPath);

        Assert.NotNull(result);
        Assert.Single(result!.Profiles);
    }
}
