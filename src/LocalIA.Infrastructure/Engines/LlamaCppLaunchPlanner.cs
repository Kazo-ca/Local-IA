using LocalIA.Core.Abstractions;
using LocalIA.Core.Gguf;
using LocalIA.Core.Models;

namespace LocalIA.Infrastructure.Engines;

public sealed class LlamaCppLaunchPlanner(IGgufMetadataReader ggufReader) : ILlamaCppLaunchPlanner
{
    public Task<LlamaCppLaunchSettings> BuildAsync(ModelTier tier, AppConfig config, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(config.LlamaCppServer.ExecutablePath) || !File.Exists(config.LlamaCppServer.ExecutablePath))
        {
            throw new InvalidOperationException("llama-server.exe non configuré ou introuvable — configure son chemin dans Paramètres.");
        }

        var totalMoeLayers = 0;
        var localPath = tier.LlamaCppSource?.LocalFilePath;
        if (!string.IsNullOrWhiteSpace(localPath) && File.Exists(localPath))
        {
            try
            {
                totalMoeLayers = ggufReader.Read(localPath).MoeLayers.Count();
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                // Non bloquant, même choix que ModelConfigurationViewModel.StartLlamaCppAsync : le
                // lancement peut se faire sans placement MoE précis (--n-cpu-moe/--override-tensor
                // seront simplement absents des arguments).
            }
        }

        var settings = LlamaCppLaunchSettingsFactory.FromTier(
            tier, config.LlamaCppServer.ExecutablePath, config.Preferences.HuggingFaceApiToken, totalMoeLayers);
        return Task.FromResult(settings);
    }
}
