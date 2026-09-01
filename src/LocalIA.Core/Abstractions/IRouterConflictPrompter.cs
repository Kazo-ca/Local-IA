namespace LocalIA.Core.Abstractions;

public enum RouterConflictResolution
{
    DropExisting,
    CancelIncoming,
}

/// <summary>Contexte affiché à l'utilisateur pour décider d'un conflit de ressources : le modèle
/// entrant, et le(s) modèle(s) actuellement chargé(s) qui bloquent (occupés au sens du routeur,
/// ou chargés hors routeur).</summary>
public sealed record RouterConflictContext(string IncomingModelId, string IncomingTierLabel, IReadOnlyList<string> BlockingModelLabels);

/// <summary>
/// Demande à l'utilisateur d'arbitrer un conflit de ressources que l'arbitre ne peut pas
/// résoudre seul (libérer les occupants bloquants, ou annuler la requête entrante). Implémenté
/// côté App (a besoin d'une fenêtre/du thread UI) ; appelé depuis un thread de requête en arrière-
/// plan du routeur.
/// </summary>
public interface IRouterConflictPrompter
{
    Task<RouterConflictResolution> PromptAsync(RouterConflictContext context, CancellationToken ct = default);
}
