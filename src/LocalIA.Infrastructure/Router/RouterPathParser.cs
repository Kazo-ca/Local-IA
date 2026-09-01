namespace LocalIA.Infrastructure.Router;

/// <summary>
/// Extrait un id de modèle optionnel d'un préfixe de chemin <c>/router/{modelId}/...</c> — le
/// reste du chemin (après le préfixe) est ce qui est transmis tel quel au backend. Si le chemin
/// ne commence pas par ce préfixe, il n'y a pas d'id dans l'URL et le chemin original est transmis
/// intact (l'id doit alors venir du corps JSON).
/// </summary>
public static class RouterPathParser
{
    private const string Prefix = "/router/";

    /// <param name="knownModelIds">
    /// Ids de modèles actuellement enregistrés (<see cref="RouterModelIndexBuilder"/>). Un id peut
    /// lui-même contenir des '/' — ex. les ids Ollama importés depuis HuggingFace,
    /// <c>hf.co/{repo}:{quant}</c> via <see cref="LocalIA.Core.Models.ModelIdentifier.GetId"/> —
    /// donc le premier segment de chemin ne suffit pas à le délimiter de façon fiable. On
    /// recherche d'abord le plus long id connu qui correspond en préfixe exact de segment (suivi
    /// d'un '/' ou de la fin du chemin) ; à défaut (id pas/plus configuré), on retombe sur
    /// l'ancienne heuristique du premier segment.
    /// </param>
    public static (string? ModelId, string RemainderPath) Parse(string path, IEnumerable<string> knownModelIds)
    {
        if (!path.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return (null, path);
        }

        var afterPrefix = path[Prefix.Length..];

        string? bestMatch = null;
        foreach (var candidate in knownModelIds)
        {
            if (afterPrefix.Length < candidate.Length || !afterPrefix.StartsWith(candidate, StringComparison.Ordinal))
            {
                continue;
            }

            if (afterPrefix.Length > candidate.Length && afterPrefix[candidate.Length] != '/')
            {
                continue;
            }

            if (bestMatch is null || candidate.Length > bestMatch.Length)
            {
                bestMatch = candidate;
            }
        }

        if (bestMatch is not null)
        {
            var matchedRemainder = afterPrefix.Length == bestMatch.Length ? "/" : afterPrefix[bestMatch.Length..];
            return (bestMatch, matchedRemainder);
        }

        var nextSlash = afterPrefix.IndexOf('/');
        if (nextSlash < 0)
        {
            var soleModelId = afterPrefix.Length > 0 ? Uri.UnescapeDataString(afterPrefix) : null;
            return (soleModelId, "/");
        }

        var modelId = afterPrefix[..nextSlash];
        var remainder = afterPrefix[nextSlash..];
        return (modelId.Length > 0 ? Uri.UnescapeDataString(modelId) : null, remainder);
    }
}
