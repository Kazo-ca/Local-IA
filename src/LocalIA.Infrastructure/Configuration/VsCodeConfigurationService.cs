using System.Text.Json;
using System.Text.Json.Nodes;
using LocalIA.Core.Abstractions;
using LocalIA.Core.Models;
using Microsoft.Extensions.Logging;

namespace LocalIA.Infrastructure.Configuration;

public sealed class VsCodeConfigurationService(ILogger<VsCodeConfigurationService> logger) : IVsCodeConfigurationService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string ChatLanguageModelsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Code", "User", "chatLanguageModels.json");

    public async Task ActivateProfileAsync(AppConfig config, ModelProfile profile, ModelTier tier, CancellationToken ct = default)
    {
        var repoRoot = RepoRootLocator.TryFind(AppContext.BaseDirectory);
        if (repoRoot is null)
        {
            // Pas de dépôt trouvé (exécutable publié seul) : .vscode/settings.json n'a de sens que
            // pour un développeur qui a le dépôt ouvert dans VS Code, donc rien à faire.
            return;
        }

        var settingsPath = Path.Combine(repoRoot, ".vscode", "settings.json");
        JsonObject settings;
        try
        {
            settings = File.Exists(settingsPath)
                ? JsonNode.Parse(await File.ReadAllTextAsync(settingsPath, ct)) as JsonObject ?? []
                : [];
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "{Path} illisible, il sera remplacé.", settingsPath);
            settings = [];
        }

        var activeModel = tier.Engine == EngineKind.LlamaCpp
            ? tier.LlamaCppSource?.LocalFilePath is { } path ? Path.GetFileNameWithoutExtension(path) : tier.Label
            : tier.OllamaCustomModelName ?? tier.OllamaBaseModel ?? tier.Label;

        var endpoint = tier.Engine == EngineKind.LlamaCpp
            ? $"http://{config.LlamaCppServer.DefaultHost}:{config.LlamaCppServer.DefaultPort}/v1"
            : $"http://{config.OllamaServer.Host}/v1";

        settings["ollama.endpoint"] = $"http://{config.OllamaServer.Host}";
        settings["localia.activeProfile"] = profile.Name;
        settings["localia.activeTier"] = tier.Label;
        settings["localia.activeModel"] = activeModel;
        settings["localia.endpoint"] = endpoint;

        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        var tempPath = settingsPath + ".tmp";
        await File.WriteAllTextAsync(tempPath, settings.ToJsonString(JsonOptions), ct);
        if (File.Exists(settingsPath))
        {
            File.Copy(settingsPath, settingsPath + ".backup", overwrite: true);
        }

        File.Move(tempPath, settingsPath, overwrite: true);
        logger.LogInformation("{Path} mis à jour pour le profil « {Profile} » ({Tier}).", settingsPath, profile.Name, tier.Label);
    }

    public async Task<VsCodeSyncReport> SyncChatLanguageModelsAsync(AppConfig config, CancellationToken ct = default)
    {
        var path = ChatLanguageModelsPath;
        var existed = File.Exists(path);
        var (merged, ollamaCount, llamaCppCount) = await BuildMergedChatLanguageModelsAsync(config, ct);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tempPath = path + ".tmp";
        await File.WriteAllTextAsync(tempPath, merged.ToJsonString(JsonOptions), ct);
        if (existed)
        {
            File.Copy(path, path + ".backup", overwrite: true);
        }

        File.Move(tempPath, path, overwrite: true);
        logger.LogInformation(
            "{Path} synchronisé : {OllamaCount} modèle(s) Ollama, {LlamaCppCount} modèle(s) llama.cpp.",
            path, ollamaCount, llamaCppCount);

        return new VsCodeSyncReport(ollamaCount, llamaCppCount);
    }

    public async Task<string> BuildChatLanguageModelsPreviewAsync(AppConfig config, CancellationToken ct = default)
    {
        // Lecture seule : contrairement à SyncChatLanguageModelsAsync, ne modifie jamais
        // chatLanguageModels.json — sert uniquement à afficher à l'utilisateur ce qu'il obtiendrait
        // en cliquant sur Synchroniser, pour qu'il puisse le copier/coller lui-même s'il préfère ne
        // pas laisser l'app toucher directement à ses fichiers VS Code.
        var (merged, _, _) = await BuildMergedChatLanguageModelsAsync(config, ct);
        return merged.ToJsonString(JsonOptions);
    }

    private async Task<(JsonArray Merged, int OllamaCount, int LlamaCppCount)> BuildMergedChatLanguageModelsAsync(AppConfig config, CancellationToken ct)
    {
        var path = ChatLanguageModelsPath;
        JsonArray root = [];
        if (File.Exists(path))
        {
            try
            {
                root = JsonNode.Parse(await File.ReadAllTextAsync(path, ct)) as JsonArray ?? [];
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "{Path} illisible, il sera remplacé.", path);
            }
        }

        // Conserve tous les providers existants (Google, OpenRouter, Copilot...) sauf les deux blocs
        // que cette méthode possède et régénère entièrement à partir de la configuration actuelle.
        var preserved = new JsonArray();
        foreach (var node in root)
        {
            if (node?["name"]?.GetValue<string>() is "Ollama" or "LlamaCpp-MoE")
            {
                continue;
            }

            preserved.Add(node?.DeepClone());
        }

        var ollamaModels = BuildOllamaModels(config);
        var llamaCppModels = BuildLlamaCppModels(config);

        if (ollamaModels.Count > 0)
        {
            preserved.Add(BuildProvider("Ollama", "ollama", ollamaModels));
        }

        if (llamaCppModels.Count > 0)
        {
            preserved.Add(BuildProvider("LlamaCpp-MoE", "llama", llamaCppModels));
        }

        return (preserved, ollamaModels.Count, llamaCppModels.Count);
    }

    public void OpenChatLanguageModelsInVsCode()
    {
        // UseShellExecute : passe par le `code` du PATH utilisateur comme le ferait un terminal,
        // sans dépendre d'un chemin d'installation VS Code codé en dur.
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("code", $"\"{ChatLanguageModelsPath}\"")
        {
            UseShellExecute = true,
        });
    }

    private static JsonObject BuildProvider(string name, string apiKey, JsonArray models) => new()
    {
        ["name"] = name,
        ["vendor"] = "customendpoint",
        ["apiKey"] = apiKey,
        ["apiType"] = "chat-completions",
        ["models"] = models,
    };

    private const int DefaultMaxOutputTokens = 8192;

    private JsonArray BuildOllamaModels(AppConfig config)
    {
        var models = new JsonArray
        {
            BuildModel("local-ia-active:latest", "Local IA (profil actif)", $"http://{config.OllamaServer.Host}/v1", toolCalling: true, contextSize: null),
        };

        foreach (var (profile, tier) in EnumerateTiers(config, EngineKind.Ollama))
        {
            if (ModelIdentifier.GetId(tier) is not { } id)
            {
                continue;
            }

            models.Add(BuildModel(id, $"{profile.Name} — {tier.Label}", $"http://{config.OllamaServer.Host}/v1", tier.ToolCalling, tier.Settings.ContextMemory.ContextSize));
        }

        return models;
    }

    private JsonArray BuildLlamaCppModels(AppConfig config)
    {
        var models = new JsonArray();
        var endpoint = $"http://{config.LlamaCppServer.DefaultHost}:{config.LlamaCppServer.DefaultPort}/v1";

        // Plusieurs paliers peuvent pointer vers le même fichier GGUF (ex. réglages MoE distincts
        // pour le même modèle) : dédoublonner par id, sinon chatLanguageModels.json contient deux
        // entrées avec le même id, ce que VS Code n'attend pas. Impossible de savoir statiquement
        // lequel des paliers en doublon correspond au serveur llama.cpp réellement lancé (l'app ne
        // suit pas de « palier actif » pour llama.cpp, un seul processus tournant à la fois) — on
        // garde donc celui au plus grand contexte configuré, le choix le moins susceptible de sous-
        // annoncer la vraie capacité à VS Code, et on journalise le doublon écarté pour qu'il ne
        // disparaisse pas silencieusement.
        var byId = new Dictionary<string, (ModelProfile Profile, ModelTier Tier)>();
        foreach (var (profile, tier) in EnumerateTiers(config, EngineKind.LlamaCpp))
        {
            var id = ModelIdentifier.GetId(tier)!;

            if (byId.TryGetValue(id, out var existing))
            {
                var keepExisting = (existing.Tier.Settings.ContextMemory.ContextSize ?? ModelIdentifier.DefaultContextSize)
                    >= (tier.Settings.ContextMemory.ContextSize ?? ModelIdentifier.DefaultContextSize);
                logger.LogWarning(
                    "Paliers en doublon pour {Id} : « {Profile1}/{Tier1} » (contexte {Context1}) vs « {Profile2}/{Tier2} » (contexte {Context2}) — {Kept} conservé pour chatLanguageModels.json.",
                    id, existing.Profile.Name, existing.Tier.Label, existing.Tier.Settings.ContextMemory.ContextSize,
                    profile.Name, tier.Label, tier.Settings.ContextMemory.ContextSize,
                    keepExisting ? existing.Tier.Label : tier.Label);

                if (!keepExisting)
                {
                    byId[id] = (profile, tier);
                }

                continue;
            }

            byId[id] = (profile, tier);
        }

        foreach (var (id, (profile, tier)) in byId)
        {
            models.Add(BuildModel(id, $"llama.cpp — {profile.Name} ({tier.Label})", endpoint, tier.ToolCalling, tier.Settings.ContextMemory.ContextSize));
        }

        return models;
    }

    private static JsonObject BuildModel(string id, string name, string url, bool toolCalling, int? contextSize) => new()
    {
        ["id"] = id,
        ["name"] = name,
        ["url"] = url,
        ["toolCalling"] = toolCalling,
        ["vision"] = false,
        ["maxInputTokens"] = contextSize ?? ModelIdentifier.DefaultContextSize,
        ["maxOutputTokens"] = Math.Min(contextSize ?? ModelIdentifier.DefaultContextSize, DefaultMaxOutputTokens),
    };

    private static IEnumerable<(ModelProfile Profile, ModelTier Tier)> EnumerateTiers(AppConfig config, EngineKind engine) =>
        config.Profiles.SelectMany(p => p.Tiers.Where(t => t.Engine == engine).Select(t => (p, t)));
}
