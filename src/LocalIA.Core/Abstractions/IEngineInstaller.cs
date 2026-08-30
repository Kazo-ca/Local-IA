namespace LocalIA.Core.Abstractions;

/// <summary>
/// Installe un moteur d'inférence absent plutôt que de se contenter d'indiquer qu'il manque — le
/// but est un vrai bouton « Installer », pas une invite renvoyant l'utilisateur télécharger et
/// configurer les choses à la main.
/// </summary>
public interface IEngineInstaller
{
    /// <summary>
    /// Télécharge l'installeur officiel Windows d'Ollama et le lance — l'installation elle-même
    /// (licence, emplacement, élévation UAC) reste pilotée par l'installeur réel affiché à
    /// l'utilisateur, jamais silencieuse. Retourne une fois l'installeur lancé (pas terminé).
    /// </summary>
    Task LaunchOllamaInstallerAsync(IProgress<string>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// Télécharge la dernière release CUDA 12.4 de llama.cpp (binaires + runtime CUDA) depuis
    /// GitHub et l'extrait dans le dossier `bin/llama-cpp` du dépôt (ou, à défaut de dépôt trouvé,
    /// `%LOCALAPPDATA%\LocalIA\llama-cpp`). Retourne le chemin complet de llama-server.exe une
    /// fois l'extraction terminée — l'appelant n'a pas à connaître l'emplacement choisi.
    /// </summary>
    Task<string> InstallLlamaCppAsync(IProgress<string>? progress = null, CancellationToken ct = default);
}
