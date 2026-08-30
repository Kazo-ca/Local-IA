using LocalIA.Core.Models;

namespace LocalIA.Core.Abstractions;

/// <summary>
/// Équivalent C# de scripts/Set-ActiveProfile.ps1 (bloc VS Code) et scripts/Sync-ChatLanguageModels.ps1
/// du toolkit PowerShell legacy : fait connaître à VS Code (settings.json du dépôt et l'extension
/// Copilot Chat via chatLanguageModels.json) le profil actif et les modèles locaux réellement
/// configurés, sans que l'utilisateur ait à éditer ces fichiers à la main.
/// </summary>
public interface IVsCodeConfigurationService
{
    /// <summary>
    /// Fusionne les clés localia.activeProfile/activeTier/activeModel/endpoint (et ollama.endpoint)
    /// dans .vscode/settings.json à la racine du dépôt, en préservant toutes les autres clés déjà
    /// présentes. Ne fait rien si l'app ne tourne pas depuis une copie du dépôt (pas de .vscode
    /// pertinent pour un utilisateur d'un exécutable publié seul).
    /// </summary>
    Task ActivateProfileAsync(AppConfig config, ModelProfile profile, ModelTier tier, CancellationToken ct = default);

    /// <summary>
    /// Synchronise les blocs de providers "Ollama" et "LlamaCpp-MoE" de
    /// %APPDATA%\Code\User\chatLanguageModels.json avec les profils/paliers de <paramref name="config"/>,
    /// en préservant tous les autres providers (Google, OpenRouter, Copilot...) déjà déclarés.
    /// </summary>
    Task<VsCodeSyncReport> SyncChatLanguageModelsAsync(AppConfig config, CancellationToken ct = default);

    /// <summary>
    /// Calcule exactement le même JSON que SyncChatLanguageModelsAsync écrirait, sans jamais
    /// toucher au fichier — pour un utilisateur qui préfère copier/coller lui-même la config dans
    /// VS Code plutôt que de laisser l'app écrire directement dans son profil VS Code.
    /// </summary>
    Task<string> BuildChatLanguageModelsPreviewAsync(AppConfig config, CancellationToken ct = default);

    /// <summary>Ouvre chatLanguageModels.json dans VS Code (équivalent du menu [O] du script legacy).</summary>
    void OpenChatLanguageModelsInVsCode();
}

public sealed record VsCodeSyncReport(int OllamaModelsWritten, int LlamaCppModelsWritten);
