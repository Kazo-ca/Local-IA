using System.Globalization;
using System.Reflection;

namespace LocalIA.Core.Configuration;

/// <summary>
/// Génère la liste d'arguments CLI de llama-server.exe par réflexion sur les groupes de
/// <see cref="ModelTierSettings"/> : ajouter un flag ne demande qu'une propriété + [EngineFlag]
/// dans le groupe concerné, cette classe n'a pas besoin d'être modifiée.
/// </summary>
public static class LlamaCppArgumentBuilder
{
    public static List<string> Build(ModelTierSettings settings, int totalMoeLayers = 0)
    {
        var args = new List<string>();

        foreach (var group in settings.GetEngineFlagGroups())
        {
            foreach (var property in group.GetType().GetProperties())
            {
                if (property.PropertyType == typeof(GpuLayerSpec))
                {
                    continue;
                }

                var attribute = property.GetCustomAttribute<EngineFlagAttribute>();
                if (attribute?.LlamaCppFlag is null)
                {
                    continue;
                }

                var value = property.GetValue(group);
                if (value is null)
                {
                    continue;
                }

                AppendArg(args, attribute.LlamaCppFlag, value, property.Name);
            }
        }

        if (settings.GpuOffload.GpuLayers is { } gpuLayers)
        {
            args.Add("-ngl");
            args.Add(gpuLayers.Mode switch
            {
                GpuLayerMode.Auto => "auto",
                GpuLayerMode.All => "all",
                _ => (gpuLayers.ExplicitCount ?? 0).ToString(CultureInfo.InvariantCulture),
            });
        }

        args.AddRange(settings.MoeOffload.BuildLlamaCppArgs(totalMoeLayers));

        if (!string.IsNullOrWhiteSpace(settings.Raw.ExtraLlamaCppArgs))
        {
            args.AddRange(SplitArguments(settings.Raw.ExtraLlamaCppArgs));
        }

        return args;
    }

    private static void AppendArg(List<string> args, string flag, object value, string propertyName)
    {
        switch (value)
        {
            case bool boolValue:
                if (boolValue)
                {
                    args.Add(flag);
                }

                break;

            case Enum enumValue:
                args.Add(flag);
                args.Add(EnumWireFormat.ToWire(enumValue));
                break;

            case string stringValue when propertyName == nameof(SamplingSettings.StopSequences):
                foreach (var line in stringValue.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    args.Add(flag);
                    args.Add(line);
                }

                break;

            case double doubleValue:
                args.Add(flag);
                args.Add(doubleValue.ToString(CultureInfo.InvariantCulture));
                break;

            default:
                args.Add(flag);
                args.Add(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");
                break;
        }
    }

    private static IEnumerable<string> SplitArguments(string raw)
    {
        // Découpage simple respectant les guillemets, pour les chemins contenant des espaces.
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        foreach (var c in raw)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }
}
