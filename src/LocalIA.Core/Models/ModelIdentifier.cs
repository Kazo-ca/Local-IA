namespace LocalIA.Core.Models;

/// <summary>
/// Dérive l'id "modèle" d'un palier de façon cohérente partout où l'app en a besoin (routeur,
/// sync VS Code) — évite que ces deux consommateurs finissent par diverger sur ce qu'est
/// "l'id" d'un même palier.
/// </summary>
public static class ModelIdentifier
{
    /// <summary>
    /// Ollama : uniquement <see cref="ModelTier.OllamaCustomModelName"/> — c'est l'alias créé
    /// spécifiquement pour ce palier (system prompt, réglages...) ; contrairement à
    /// <see cref="ModelTier.OllamaBaseModel"/>, qui peut être partagé par plusieurs paliers, il
    /// identifie ce palier de façon unique. Null si absent (palier pas encore "activé" côté
    /// Ollama) — l'appelant doit alors exclure ce palier plutôt que deviner un id ambigu.
    /// llama.cpp : nom du fichier GGUF local sans extension, ou le libellé du palier en repli
    /// si aucun fichier local n'est configuré (jamais null).
    /// </summary>
    public static string? GetId(ModelTier tier) => tier.Engine switch
    {
        EngineKind.Ollama => tier.OllamaCustomModelName is { Length: > 0 } id ? id : null,
        EngineKind.LlamaCpp => tier.LlamaCppSource?.LocalFilePath is { } path
            ? Path.GetFileNameWithoutExtension(path)
            : tier.Label,
        _ => null,
    };
}
