using System.Reflection;

namespace LocalIA.Core.Configuration;

/// <summary>
/// Génère l'objet "parameters" du corps JSON de POST /api/create par réflexion sur les groupes de
/// <see cref="ModelTierSettings"/> — même mécanisme que <see cref="LlamaCppArgumentBuilder"/>.
/// </summary>
public static class OllamaParameterBuilder
{
    public static Dictionary<string, object> Build(ModelTierSettings settings)
    {
        var parameters = new Dictionary<string, object>();

        foreach (var group in settings.GetEngineFlagGroups())
        {
            foreach (var property in group.GetType().GetProperties())
            {
                if (property.PropertyType == typeof(GpuLayerSpec))
                {
                    continue;
                }

                var attribute = property.GetCustomAttribute<EngineFlagAttribute>();
                if (attribute?.OllamaParameter is null)
                {
                    continue;
                }

                var value = property.GetValue(group);
                if (value is null)
                {
                    continue;
                }

                parameters[attribute.OllamaParameter] = FormatValue(value, property.Name);
            }
        }

        // Ollama n'a pas de sentinelle "auto"/"all" pour num_gpu : uniquement un compte explicite.
        if (settings.GpuOffload.GpuLayers is { Mode: GpuLayerMode.Explicit, ExplicitCount: { } count })
        {
            parameters["num_gpu"] = count;
        }

        if (!string.IsNullOrWhiteSpace(settings.Raw.ExtraOllamaParameters))
        {
            foreach (var line in settings.Raw.ExtraOllamaParameters.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split(' ', 2, StringSplitOptions.TrimEntries);
                if (parts.Length == 2)
                {
                    parameters[parts[0]] = parts[1];
                }
            }
        }

        return parameters;
    }

    private static object FormatValue(object value, string propertyName) => value switch
    {
        Enum enumValue => EnumWireFormat.ToWire(enumValue),
        string stringValue when propertyName == nameof(SamplingSettings.StopSequences) =>
            stringValue.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        _ => value,
    };

}
