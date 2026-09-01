using LocalIA.Core.Models;

namespace LocalIA.Core.Abstractions;

/// <summary>
/// Décide s'il y a la place pour charger le modèle résolu, quoi libérer si besoin (parmi les
/// paliers que le routeur suit lui-même — jamais un modèle chargé manuellement), et charge
/// effectivement le moteur cible. Point d'entrée unique partagé par Ollama et llama.cpp — voir
/// RouterResourceArbiter pour la logique détaillée (contention VRAM cross-moteur + contrainte
/// dure d'instance unique côté llama.cpp).
/// </summary>
public interface IRouterResourceArbiter
{
    Task<RouterLoadResult> EnsureLoadedAsync(RouterModelResolution resolution, CancellationToken ct = default);
}
