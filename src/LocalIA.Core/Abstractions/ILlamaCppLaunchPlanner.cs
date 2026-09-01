using LocalIA.Core.Models;

namespace LocalIA.Core.Abstractions;

/// <summary>
/// Partie non interactive de la construction des paramètres de lancement llama.cpp pour un
/// palier : lecture GGUF pour le placement MoE puis <see cref="LlamaCppLaunchSettingsFactory.FromTier"/>.
/// Extrait de <c>ModelConfigurationViewModel.StartLlamaCppAsync</c> pour être réutilisable par le
/// routeur, qui n'a pas d'UI pour proposer un repli interactif (sélection manuelle du fichier
/// GGUF) si le palier n'a aucune source valide — dans ce cas, <see cref="BuildAsync"/> échoue
/// avec une <see cref="InvalidOperationException"/> plutôt que de demander à l'utilisateur.
/// </summary>
public interface ILlamaCppLaunchPlanner
{
    Task<LlamaCppLaunchSettings> BuildAsync(ModelTier tier, AppConfig config, CancellationToken ct = default);
}
