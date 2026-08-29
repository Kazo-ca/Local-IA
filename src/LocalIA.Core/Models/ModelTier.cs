using LocalIA.Core.Configuration;

namespace LocalIA.Core.Models;

public enum ModelfileMode
{
    Legacy,
    Native,
}

public sealed class ModelfileSource
{
    public ModelfileMode Mode { get; set; } = ModelfileMode.Native;

    /// <summary>Chemin (relatif à la racine du dépôt legacy) du Modelfile d'origine, si importé.</summary>
    public string? LegacyPath { get; set; }
}

public sealed class LlamaCppModelSource
{
    public string? HfRepoId { get; set; }
    public string? HfFile { get; set; }
    public string? QuantHint { get; set; }
    public string? LocalFilePath { get; set; }
}

public sealed class ModelTier
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Label { get; set; } = "";
    public EngineKind Engine { get; set; } = EngineKind.Ollama;

    public string? OllamaBaseModel { get; set; }
    public string? OllamaCustomModelName { get; set; }

    public LlamaCppModelSource? LlamaCppSource { get; set; }

    public ModelfileSource ModelfileSource { get; set; } = new();
    public string? SystemPrompt { get; set; }
    public string? ChatTemplate { get; set; }
    public bool ToolCalling { get; set; }
    public string? VramOffloadDescription { get; set; }

    public ModelTierSettings Settings { get; set; } = new();

    public override string ToString() => Label;
}
