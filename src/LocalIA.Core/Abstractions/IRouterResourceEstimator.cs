using LocalIA.Core.Models;

namespace LocalIA.Core.Abstractions;

/// <summary>Estime l'empreinte VRAM d'un palier pour la décision d'admission de l'arbitre de
/// ressources du routeur — une approximation volontairement simple (pas le placement précis par
/// couche du conseiller MoE), suffisante pour éviter un surengagement grossier de la VRAM.</summary>
public interface IRouterResourceEstimator
{
    /// <summary>Null si l'empreinte ne peut pas être estimée (tag Ollama jamais téléchargé,
    /// fichier GGUF illisible...) — l'appelant doit alors laisser passer sans blocage plutôt que
    /// de bloquer sur une inconnue.</summary>
    Task<long?> EstimateVramBytesAsync(ModelTier tier, CancellationToken ct = default);
}
