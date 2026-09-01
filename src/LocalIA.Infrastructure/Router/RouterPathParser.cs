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

    public static (string? ModelId, string RemainderPath) Parse(string path)
    {
        if (!path.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return (null, path);
        }

        var afterPrefix = path[Prefix.Length..];
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
