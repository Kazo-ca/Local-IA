namespace LocalIA.Core.Configuration;

/// <summary>Échappatoire pour tout flag non encore modélisé par un champ typé dédié.</summary>
public sealed class RawOverrides
{
    /// <summary>Arguments CLI supplémentaires pour llama-server.exe, ajoutés après tous les flags générés.</summary>
    public string? ExtraLlamaCppArgs { get; set; }

    /// <summary>Lignes PARAMETER Ollama supplémentaires, une par ligne, format "clé valeur".</summary>
    public string? ExtraOllamaParameters { get; set; }
}
