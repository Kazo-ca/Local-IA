namespace LocalIA.Core.HuggingFace;

public enum GgufFileRole
{
    MainWeights,
    MultimodalProjector,
    DraftModel,
}

/// <summary>
/// Certains dépôts Hugging Face regroupent, à côté du modèle principal, des fichiers GGUF
/// auxiliaires : un projecteur multimodal ("mmproj-*") pour la vision, ou un petit modèle
/// brouillon ("mtp-*"/"draft-*") pour le décodage spéculatif. Ils ne sont pas des quantifications
/// alternatives du même modèle et doivent être proposés séparément plutôt que mélangés à la
/// liste des choix de quantification principale.
/// </summary>
public static class GgufFileRoleClassifier
{
    public static GgufFileRole Classify(string fileName)
    {
        var lower = fileName.ToLowerInvariant();

        if (lower.Contains("mmproj", StringComparison.Ordinal))
        {
            return GgufFileRole.MultimodalProjector;
        }

        if (lower.Contains("mtp-", StringComparison.Ordinal) || lower.Contains("-mtp", StringComparison.Ordinal)
            || lower.Contains("draft-", StringComparison.Ordinal) || lower.Contains("-draft", StringComparison.Ordinal))
        {
            return GgufFileRole.DraftModel;
        }

        return GgufFileRole.MainWeights;
    }

    public static string DisplayName(GgufFileRole role) => role switch
    {
        GgufFileRole.MultimodalProjector => "Projecteur multimodal (vision)",
        GgufFileRole.DraftModel => "Modèle brouillon (décodage spéculatif MTP)",
        _ => "Modèle principal",
    };

    public static string Description(GgufFileRole role) => role switch
    {
        GgufFileRole.MultimodalProjector =>
            "Permet d'envoyer des images au modèle. llama.cpp le télécharge et l'active automatiquement quand le modèle principal est lancé via -hf (sauf --no-mmproj) ; l'associer ici fixe un chemin local explicite.",
        GgufFileRole.DraftModel =>
            "Petit modèle qui propose plusieurs tokens à l'avance pour accélérer la génération du modèle principal (décodage spéculatif), sans perte de qualité si le modèle principal valide chaque proposition.",
        _ => "",
    };
}
