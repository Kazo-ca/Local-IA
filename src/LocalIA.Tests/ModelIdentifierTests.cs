using LocalIA.Core.Models;

namespace LocalIA.Tests;

public class ModelIdentifierTests
{
    [Fact]
    public void GetId_Ollama_UsesCustomModelName()
    {
        var tier = new ModelTier { Engine = EngineKind.Ollama, OllamaCustomModelName = "local-ia-csharp:32b", Label = "max_accuracy" };

        Assert.Equal("local-ia-csharp:32b", ModelIdentifier.GetId(tier));
    }

    [Fact]
    public void GetId_Ollama_ReturnsNull_WhenNoCustomModelName()
    {
        // Un palier Ollama sans alias personnalisé n'est pas identifiable de façon unique
        // (OllamaBaseModel peut être partagé par plusieurs paliers) — jamais deviner un id ici.
        var tier = new ModelTier { Engine = EngineKind.Ollama, OllamaBaseModel = "qwen2.5-coder:32b", Label = "max_accuracy" };

        Assert.Null(ModelIdentifier.GetId(tier));
    }

    [Fact]
    public void GetId_LlamaCpp_UsesFileNameWithoutExtension()
    {
        var tier = new ModelTier
        {
            Engine = EngineKind.LlamaCpp,
            Label = "max_accuracy",
            LlamaCppSource = new LlamaCppModelSource { LocalFilePath = @"E:\llama_models\qwen3.6-35b-a3b-q4_k_m.gguf" },
        };

        Assert.Equal("qwen3.6-35b-a3b-q4_k_m", ModelIdentifier.GetId(tier));
    }

    [Fact]
    public void GetId_LlamaCpp_FallsBackToLabel_WhenNoLocalFile()
    {
        var tier = new ModelTier { Engine = EngineKind.LlamaCpp, Label = "max_accuracy" };

        Assert.Equal("max_accuracy", ModelIdentifier.GetId(tier));
    }
}
