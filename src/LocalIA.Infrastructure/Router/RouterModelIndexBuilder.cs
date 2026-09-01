using LocalIA.Core.Models;
using Microsoft.Extensions.Logging;

namespace LocalIA.Infrastructure.Router;

/// <summary>
/// Construit l'index id-de-modèle → résolution à partir de la configuration courante. Logique
/// pure (pas d'E/S) pour rester facilement testable ; <see cref="RouterModelResolver"/> l'appelle
/// et met le résultat en cache.
/// </summary>
public static class RouterModelIndexBuilder
{
    public static IReadOnlyDictionary<string, RouterModelResolution> Build(AppConfig config, ILogger logger)
    {
        var index = new Dictionary<string, RouterModelResolution>();
        var (ollamaHost, ollamaPort) = ParseHostPort(config.OllamaServer.Host, defaultPort: 11434);

        foreach (var profile in config.Profiles)
        {
            foreach (var tier in profile.Tiers)
            {
                if (ModelIdentifier.GetId(tier) is not { } id)
                {
                    continue;
                }

                RouterModelResolution resolution;
                if (tier.Engine == EngineKind.Ollama)
                {
                    resolution = new RouterModelResolution(id, profile, tier, EngineKind.Ollama, ollamaHost, ollamaPort);
                }
                else
                {
                    var host = string.IsNullOrWhiteSpace(tier.Settings.ServerNetworking.Host)
                        ? config.LlamaCppServer.DefaultHost
                        : tier.Settings.ServerNetworking.Host!;
                    var port = tier.Settings.ServerNetworking.Port ?? config.LlamaCppServer.DefaultPort;
                    resolution = new RouterModelResolution(id, profile, tier, EngineKind.LlamaCpp, host, port);
                }

                if (index.TryGetValue(id, out var existing))
                {
                    // Même tie-break que VsCodeConfigurationService.BuildLlamaCppModels : garder le
                    // palier au plus grand contexte configuré est le choix le moins susceptible de
                    // sous-annoncer la vraie capacité, et on journalise le doublon écarté.
                    var existingContext = existing.Tier.Settings.ContextMemory.ContextSize ?? 0;
                    var newContext = tier.Settings.ContextMemory.ContextSize ?? 0;
                    if (newContext <= existingContext)
                    {
                        logger.LogWarning(
                            "Id de modèle en doublon « {Id} » : « {Profile1}/{Tier1} » conservé, « {Profile2}/{Tier2} » écarté pour le routeur.",
                            id, existing.Profile.Name, existing.Tier.Label, profile.Name, tier.Label);
                        continue;
                    }

                    logger.LogWarning(
                        "Id de modèle en doublon « {Id} » : « {Profile2}/{Tier2} » conservé, « {Profile1}/{Tier1} » écarté pour le routeur.",
                        id, profile.Name, tier.Label, existing.Profile.Name, existing.Tier.Label);
                }

                index[id] = resolution;
            }
        }

        return index;
    }

    private static (string Host, int Port) ParseHostPort(string hostPort, int defaultPort)
    {
        var separatorIndex = hostPort.LastIndexOf(':');
        if (separatorIndex > 0 && int.TryParse(hostPort[(separatorIndex + 1)..], out var port))
        {
            return (hostPort[..separatorIndex], port);
        }

        return (hostPort, defaultPort);
    }
}
