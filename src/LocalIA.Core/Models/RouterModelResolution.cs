namespace LocalIA.Core.Models;

/// <summary>Résultat de la résolution d'un id de modèle vers le profil/palier qui le sert et
/// l'adresse réelle du moteur cible.</summary>
public sealed record RouterModelResolution(
    string ModelId,
    ModelProfile Profile,
    ModelTier Tier,
    EngineKind Engine,
    string TargetHost,
    int TargetPort);
