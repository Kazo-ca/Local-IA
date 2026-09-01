namespace LocalIA.Core.Models;

/// <summary>Charge concurrente actuelle d'un palier routé, pour affichage informatif au Dashboard
/// — jamais utilisé pour bloquer une requête (seule la VRAM, via l'arbitre, est une contrainte
/// bloquante).</summary>
public sealed record RouterTierLoadInfo(string ProfileName, string TierLabel, EngineKind Engine, int ActiveCount, int? Capacity);
