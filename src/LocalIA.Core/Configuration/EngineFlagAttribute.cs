namespace LocalIA.Core.Configuration;

/// <summary>
/// Associe une propriété d'un groupe de <see cref="ModelTierSettings"/> au(x) nom(s) réel(s) du
/// flag correspondant côté Ollama (clé Modelfile PARAMETER) et/ou côté llama.cpp (flag CLI).
/// Un côté laissé à null signifie que ce moteur ne supporte pas ce réglage — l'UI grise alors le
/// champ pour cet moteur plutôt que de le masquer, pour que l'utilisateur comprenne pourquoi.
/// Ajouter un nouveau flag ne demande qu'une propriété + cet attribut : les builders et l'UI le
/// prennent en charge automatiquement par réflexion, sans autre modification.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class EngineFlagAttribute : Attribute
{
    public string? OllamaParameter { get; init; }
    public string? LlamaCppFlag { get; init; }
    public string? DisplayName { get; init; }
    public bool Deprecated { get; init; }
    public string? PreferredReplacementFlag { get; init; }
}
