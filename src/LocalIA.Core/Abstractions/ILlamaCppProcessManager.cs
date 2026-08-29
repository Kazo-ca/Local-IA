using LocalIA.Core.Models;

namespace LocalIA.Core.Abstractions;

public interface ILlamaCppProcessManager : IEngineProcessManager
{
    string? CurrentModelPath { get; }

    /// <summary>
    /// Démarre llama-server avec le modèle demandé. Retourne false sans rien changer si un
    /// llama-server (possédé ou externe) tourne déjà : l'appelant doit explicitement demander
    /// l'arrêt de l'instance existante avant de réessayer, car un remplacement silencieux
    /// interromprait une session de génération potentiellement en cours.
    /// </summary>
    Task<bool> StartAsync(LlamaCppLaunchSettings settings, CancellationToken ct = default);
}
