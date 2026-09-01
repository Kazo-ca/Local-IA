using LocalIA.Core.Models;

namespace LocalIA.Infrastructure.Router;

/// <summary>Retrouve, à partir du "CurrentModelPath" brut rapporté par le gestionnaire de process
/// llama.cpp, le palier de l'index du routeur qui lui correspond — partagé par l'arbitre de
/// ressources et le balayage d'inactivité pour ne jamais diverger sur "quel palier est chargé".</summary>
internal static class LlamaCppTierLookup
{
    public static Guid? FindLoadedTierId(string currentLabel, IReadOnlyDictionary<string, RouterModelResolution> index)
    {
        foreach (var resolution in index.Values)
        {
            if (resolution.Engine != EngineKind.LlamaCpp)
            {
                continue;
            }

            var label = LlamaCppLaunchSettingsFactory.ComputeModelLabel(resolution.Tier);
            if (label is not null && string.Equals(label, currentLabel, StringComparison.OrdinalIgnoreCase))
            {
                return resolution.Tier.Id;
            }
        }

        return null;
    }
}
