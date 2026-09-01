using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.Messaging;
using LocalIA.Core.Abstractions;
using LocalIA.Core.Messaging;
using LocalIA.Core.Models;
using Microsoft.Extensions.Logging;

namespace LocalIA.Infrastructure.Configuration;

public sealed class AppConfigRepository(LegacyConfigImporter legacyImporter, IMessenger messenger, ILogger<AppConfigRepository> logger) : IAppConfigRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    private static string ConfigDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LocalIA");

    private static string ConfigFilePath => Path.Combine(ConfigDirectory, "app-config.json");

    public async Task<AppConfig> LoadAsync(CancellationToken ct = default)
    {
        if (await TryLoadFromFileAsync(ConfigFilePath, ct) is { } loaded)
        {
            return loaded;
        }

        var backupPath = ConfigFilePath + ".backup";
        if (await TryLoadFromFileAsync(backupPath, ct) is { } fromBackup)
        {
            logger.LogWarning("app-config.json illisible ou absent, restauré depuis {BackupPath}", backupPath);
            return fromBackup;
        }

        return await BuildDefaultConfigAsync(ct);
    }

    private async Task<AppConfig?> TryLoadFromFileAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var config = await JsonSerializer.DeserializeAsync<AppConfig>(stream, JsonOptions, ct);
            if (config is not null && config.SchemaVersion != AppConfig.CurrentSchemaVersion)
            {
                // Pas de migration à effectuer aujourd'hui (une seule version a jamais existé) —
                // mais au moins visible dans les logs plutôt que silencieusement ignoré, pour le
                // jour où la forme d'AppConfig change réellement.
                logger.LogWarning(
                    "{Path} a un SchemaVersion différent ({Found}, attendu {Expected}) — chargé tel quel.",
                    path, config.SchemaVersion, AppConfig.CurrentSchemaVersion);
            }

            return config;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "{Path} illisible", path);
            return null;
        }
    }

    private async Task<AppConfig> BuildDefaultConfigAsync(CancellationToken ct)
    {
        var config = new AppConfig();

        var repoRoot = RepoRootLocator.TryFind(AppContext.BaseDirectory);
        if (repoRoot is null)
        {
            return config;
        }

        try
        {
            var imported = await legacyImporter.TryImportAsync(repoRoot, ct);
            if (imported is not null)
            {
                config.Profiles = imported.Profiles;
                if (!string.IsNullOrWhiteSpace(imported.OllamaModelsPath))
                {
                    config.Storage.OllamaModelsPath = imported.OllamaModelsPath;
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            logger.LogWarning(ex, "Échec de l'import de la configuration legacy depuis {RepoRoot}", repoRoot);
        }

        return config;
    }

    public async Task<IReadOnlyList<ModelProfile>> ImportLegacyAsync(CancellationToken ct = default)
    {
        var repoRoot = RepoRootLocator.TryFind(AppContext.BaseDirectory);
        if (repoRoot is null)
        {
            return [];
        }

        try
        {
            var imported = await legacyImporter.TryImportAsync(repoRoot, ct);
            return imported?.Profiles ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            logger.LogWarning(ex, "Échec de l'import de la configuration legacy depuis {RepoRoot}", repoRoot);
            return [];
        }
    }

    public async Task SaveAsync(AppConfig config, CancellationToken ct = default)
    {
        Directory.CreateDirectory(ConfigDirectory);

        var tempPath = ConfigFilePath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, config, JsonOptions, ct);
        }

        if (File.Exists(ConfigFilePath))
        {
            File.Copy(ConfigFilePath, ConfigFilePath + ".backup", overwrite: true);
        }

        File.Move(tempPath, ConfigFilePath, overwrite: true);
        messenger.Send(new AppConfigChangedMessage(config));
    }
}
