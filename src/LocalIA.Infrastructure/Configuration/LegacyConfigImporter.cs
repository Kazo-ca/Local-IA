using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using LocalIA.Core.Configuration;
using LocalIA.Core.Models;

namespace LocalIA.Infrastructure.Configuration;

/// <summary>
/// Importe en lecture seule le config/config.json legacy et ses Modelfiles associés — jamais
/// écrit ni modifié, pour que le toolkit PowerShell continue de fonctionner sans changement.
/// </summary>
public sealed record LegacyImportResult(List<ModelProfile> Profiles, string? OllamaModelsPath);

public sealed class LegacyConfigImporter
{
    public async Task<LegacyImportResult?> TryImportAsync(string repoRootPath, CancellationToken ct = default)
    {
        var legacyConfigPath = Path.Combine(repoRootPath, "config", "config.json");
        if (!File.Exists(legacyConfigPath))
        {
            return null;
        }

        LegacyConfigDto? legacy;
        await using (var stream = File.OpenRead(legacyConfigPath))
        {
            legacy = await JsonSerializer.DeserializeAsync<LegacyConfigDto>(stream, cancellationToken: ct);
        }

        if (legacy?.Profiles is null)
        {
            return null;
        }

        var profiles = new List<ModelProfile>();
        foreach (var (profileKey, profileDto) in legacy.Profiles)
        {
            var profile = new ModelProfile
            {
                Name = profileDto.Name ?? profileKey,
                Description = profileDto.Description ?? "",
            };

            if (profileDto.Tiers is not null)
            {
                foreach (var (tierKey, tierDto) in profileDto.Tiers)
                {
                    profile.Tiers.Add(await BuildTierAsync(repoRootPath, tierKey, tierDto, ct));
                }
            }

            profiles.Add(profile);
        }

        return new LegacyImportResult(profiles, legacy.Storage?.ModelsPath);
    }

    private static async Task<ModelTier> BuildTierAsync(string repoRootPath, string tierKey, LegacyTierDto tierDto, CancellationToken ct)
    {
        string? systemPrompt = null;
        var settings = new ModelTierSettings();

        if (tierDto.Modelfile is not null)
        {
            var modelfilePath = Path.Combine(repoRootPath, tierDto.Modelfile.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(modelfilePath))
            {
                var content = await File.ReadAllTextAsync(modelfilePath, ct);
                (systemPrompt, settings) = ParseModelfile(content);
            }
        }

        return new ModelTier
        {
            Label = tierKey,
            Engine = EngineKind.Ollama,
            OllamaBaseModel = tierDto.BaseModel,
            OllamaCustomModelName = tierDto.CustomModelName,
            ModelfileSource = new ModelfileSource { Mode = ModelfileMode.Legacy, LegacyPath = tierDto.Modelfile },
            SystemPrompt = systemPrompt,
            ToolCalling = tierDto.ToolCalling,
            VramOffloadDescription = tierDto.VramOffload,
            Settings = settings,
        };
    }

    // Table inverse "nom de paramètre Ollama -> propriété" construite depuis les mêmes attributs
    // [EngineFlag] que OllamaParameterBuilder : reconnaît automatiquement tout paramètre déjà
    // modélisé ailleurs dans l'app (pas seulement les 4 clés d'origine), sans liste à maintenir
    // séparément qui risquerait de rester incomplète.
    private static readonly Dictionary<string, PropertyInfo> OllamaParameterProperties = BuildOllamaParameterMap();

    private static Dictionary<string, PropertyInfo> BuildOllamaParameterMap()
    {
        var map = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in new ModelTierSettings().GetEngineFlagGroups())
        {
            foreach (var property in group.GetType().GetProperties())
            {
                if (property.GetCustomAttribute<EngineFlagAttribute>() is { OllamaParameter: { } ollamaParameter })
                {
                    map[ollamaParameter] = property;
                }
            }
        }

        return map;
    }

    private static (string? SystemPrompt, ModelTierSettings Settings) ParseModelfile(string content)
    {
        var settings = new ModelTierSettings();
        var groupsByType = settings.GetEngineFlagGroups().ToDictionary(g => g.GetType());

        var systemMatch = Regex.Match(content, "SYSTEM\\s+\"\"\"(.*?)\"\"\"", RegexOptions.Singleline);
        var systemPrompt = systemMatch.Success ? systemMatch.Groups[1].Value.Trim() : null;

        foreach (Match match in Regex.Matches(content, @"PARAMETER\s+(\S+)\s+(\S+)"))
        {
            var key = match.Groups[1].Value;
            var value = match.Groups[2].Value;

            if (!OllamaParameterProperties.TryGetValue(key, out var property)
                || !groupsByType.TryGetValue(property.DeclaringType!, out var groupInstance))
            {
                continue; // clé PARAMETER que rien dans l'app ne modélise pour l'instant — ignorée.
            }

            var underlyingType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            object? converted = underlyingType switch
            {
                _ when underlyingType == typeof(int) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null,
                _ when underlyingType == typeof(double) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null,
                _ when underlyingType == typeof(bool) => bool.TryParse(value, out var b) ? b : null,
                _ when underlyingType == typeof(string) => value,
                _ when underlyingType.IsEnum && Enum.TryParse(underlyingType, value, ignoreCase: true, out var e) => e,
                _ => null,
            };

            if (converted is not null)
            {
                property.SetValue(groupInstance, converted);
            }
        }

        return (systemPrompt, settings);
    }

    private sealed class LegacyConfigDto
    {
        [JsonPropertyName("storage")]
        public LegacyStorageDto? Storage { get; set; }

        [JsonPropertyName("profiles")]
        public Dictionary<string, LegacyProfileDto>? Profiles { get; set; }
    }

    private sealed class LegacyStorageDto
    {
        [JsonPropertyName("modelsPath")]
        public string? ModelsPath { get; set; }
    }

    private sealed class LegacyProfileDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("tiers")]
        public Dictionary<string, LegacyTierDto>? Tiers { get; set; }
    }

    private sealed class LegacyTierDto
    {
        [JsonPropertyName("base_model")]
        public string? BaseModel { get; set; }

        [JsonPropertyName("custom_model_name")]
        public string? CustomModelName { get; set; }

        [JsonPropertyName("modelfile")]
        public string? Modelfile { get; set; }

        [JsonPropertyName("vram_offload")]
        public string? VramOffload { get; set; }

        [JsonPropertyName("tool_calling")]
        public bool ToolCalling { get; set; }
    }
}
