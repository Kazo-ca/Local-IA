using LocalIA.Core.Configuration;

namespace LocalIA.Core.Models;

/// <summary>
/// Paramètres de lancement réel d'un palier llama.cpp. `Arguments` est la liste complète déjà
/// construite (source du modèle + tout ModelTierSettings via LlamaCppArgumentBuilder) — voir
/// <see cref="LlamaCppLaunchSettingsFactory.FromTier"/>, seul point de construction recommandé.
/// </summary>
public sealed class LlamaCppLaunchSettings
{
    public required string ExecutablePath { get; init; }

    /// <summary>Étiquette du modèle actif à afficher (chemin local, ou "auteur/dépôt:quant" si résolu via -hf) — pas nécessairement un chemin de fichier réel.</summary>
    public required string ModelPath { get; init; }

    public required IReadOnlyList<string> Arguments { get; init; }
}

public static class LlamaCppLaunchSettingsFactory
{
    /// <summary>
    /// Construit la ligne de commande complète pour un palier llama.cpp : résout la source du
    /// modèle (fichier local déjà téléchargé, sinon -hf/-hff/-hft pour que llama.cpp résolve et
    /// télécharge lui-même), puis ajoute tous les flags de ModelTierSettings via
    /// LlamaCppArgumentBuilder (même générateur que l'aperçu affiché dans "Configuration du
    /// modèle" — jamais un second générateur séparé qui risquerait de diverger).
    /// </summary>
    public static LlamaCppLaunchSettings FromTier(ModelTier tier, string executablePath, string? huggingFaceApiToken, int totalMoeLayers)
    {
        var source = tier.LlamaCppSource;
        var args = new List<string>();
        string modelLabel;

        if (!string.IsNullOrWhiteSpace(source?.LocalFilePath))
        {
            args.Add("-m");
            args.Add(source.LocalFilePath);
            modelLabel = source.LocalFilePath;
        }
        else if (!string.IsNullOrWhiteSpace(source?.HfRepoId))
        {
            var repoRef = string.IsNullOrWhiteSpace(source.QuantHint) ? source.HfRepoId : $"{source.HfRepoId}:{source.QuantHint}";
            args.Add("-hf");
            args.Add(repoRef);
            modelLabel = repoRef;

            if (!string.IsNullOrWhiteSpace(source.HfFile))
            {
                args.Add("-hff");
                args.Add(source.HfFile);
            }

            if (!string.IsNullOrWhiteSpace(huggingFaceApiToken))
            {
                args.Add("-hft");
                args.Add(huggingFaceApiToken);
            }
        }
        else
        {
            throw new InvalidOperationException($"Le palier « {tier.Label} » n'a aucune source de modèle configurée (ni fichier local, ni dépôt Hugging Face).");
        }

        args.AddRange(LlamaCppArgumentBuilder.Build(tier.Settings, totalMoeLayers));

        return new LlamaCppLaunchSettings
        {
            ExecutablePath = executablePath,
            ModelPath = modelLabel,
            Arguments = args,
        };
    }
}
