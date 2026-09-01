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

        if (!string.IsNullOrWhiteSpace(source?.LocalFilePath))
        {
            if (!File.Exists(source.LocalFilePath))
            {
                throw new InvalidOperationException($"Le fichier configuré pour le palier « {tier.Label} » est introuvable : {source.LocalFilePath}");
            }

            args.Add("-m");
            args.Add(source.LocalFilePath);
        }
        else if (!string.IsNullOrWhiteSpace(source?.HfRepoId))
        {
            var repoRef = ComputeModelLabel(tier)!;
            args.Add("-hf");
            args.Add(repoRef);

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
            ModelPath = ComputeModelLabel(tier)!,
            Arguments = args,
        };
    }

    /// <summary>
    /// Ce que le "CurrentModelPath" du gestionnaire de process llama.cpp vaudra si ce palier est
    /// celui démarré (chemin local, ou "auteur/dépôt:quant" si résolu via -hf) — sans valider que
    /// le fichier existe ni construire les arguments complets. Utilisé par le routeur pour savoir
    /// si le palier demandé est déjà celui actuellement chargé, sans dupliquer StartAsync. Null si
    /// le palier n'a aucune source configurée (ni fichier local, ni dépôt Hugging Face).
    /// </summary>
    public static string? ComputeModelLabel(ModelTier tier)
    {
        var source = tier.LlamaCppSource;
        if (!string.IsNullOrWhiteSpace(source?.LocalFilePath))
        {
            return source.LocalFilePath;
        }

        if (!string.IsNullOrWhiteSpace(source?.HfRepoId))
        {
            return string.IsNullOrWhiteSpace(source.QuantHint) ? source.HfRepoId : $"{source.HfRepoId}:{source.QuantHint}";
        }

        return null;
    }
}
