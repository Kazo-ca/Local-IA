using System.Text.Json.Serialization;

namespace LocalIA.Core.Advisor;

/// <summary>Réponse structurée attendue du modèle local interrogé via le bouton "Demander à l'IA".</summary>
public sealed class AiAdvisorStructuredResponse
{
    [JsonPropertyName("agreement")]
    public string Agreement { get; set; } = "unknown";

    [JsonPropertyName("alternative_suggestion")]
    public string? AlternativeSuggestion { get; set; }

    [JsonPropertyName("justification")]
    public string Justification { get; set; } = "";

    [JsonPropertyName("risk_level")]
    public string RiskLevel { get; set; } = "unknown";
}

public static class AdvisorPromptBuilder
{
    private const string SystemPrompt =
        """
        Tu donnes un deuxième avis sur une recommandation déjà calculée par un algorithme
        déterministe pour configurer un modèle d'IA local (Ollama/llama.cpp) sur le matériel
        de l'utilisateur. Traite les chiffres fournis comme fiables (ne les recalcule pas).
        Le matériel combine VRAM (GPU) et RAM (CPU) : llama.cpp peut répartir un modèle entre les
        deux (couches d'attention/denses en VRAM, experts MoE ou couches excédentaires en RAM)
        plutôt que devoir tenir entièrement dans l'un ou l'autre — raisonne sur la capacité totale
        du matériel (VRAM + RAM disponibles ensemble), pas sur un choix binaire "tient en VRAM ou
        pas". Dis clairement si tu es d'accord ou non, et pourquoi. Réponds en français, en 3 à 5
        phrases maximum, de façon concrète et actionnable.
        """;

    public static string SystemMessage => SystemPrompt;

    public static string BuildUserMessage(
        string hardwareSummary, string modelSummary, string calculatedVerdict, IReadOnlyList<string> notes)
    {
        var notesText = notes.Count > 0 ? string.Join(" ", notes) : "(aucune)";
        return $$"""
            Matériel : {{hardwareSummary}}
            Modèle candidat : {{modelSummary}}
            Recommandation déjà calculée : {{calculatedVerdict}}
            Remarques complémentaires détectées : {{notesText}}

            Donne ton avis (accord/désaccord/nuance), et si tu ne recalcules rien, explique en
            langage clair ce que ça implique concrètement pour l'utilisateur.
            """;
    }

    public const string JsonSchema =
        """
        {
          "type": "object",
          "properties": {
            "agreement": { "type": "string", "enum": ["agree", "disagree", "partially_agree"] },
            "alternative_suggestion": { "type": ["string", "null"] },
            "justification": { "type": "string" },
            "risk_level": { "type": "string", "enum": ["low", "medium", "high"] }
          },
          "required": ["agreement", "justification", "risk_level"]
        }
        """;
}
