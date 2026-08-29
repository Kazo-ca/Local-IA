using System.Text;
using System.Text.RegularExpressions;

namespace LocalIA.Core.Gguf;

public interface IGgufMetadataReader
{
    GgufModelMetadata Read(string filePath);
}

/// <summary>
/// Lecteur GGUF autonome (pas de binding llama.cpp) : ne lit que l'en-tête, les métadonnées et les
/// infos de tenseurs (quelques Ko à quelques Mo), jamais les poids eux-mêmes. Format documenté et
/// stable : magic "GGUF" + version + compteurs, puis paires clé/valeur, puis infos de tenseurs.
/// </summary>
public sealed class GgufMetadataReader : IGgufMetadataReader
{
    private const uint GgufMagic = 0x46554747; // "GGUF" en little-endian
    private static readonly Regex LayerIndexPattern = new(@"^blk\.(\d+)\.", RegexOptions.Compiled);

    public GgufModelMetadata Read(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        var magic = reader.ReadUInt32();
        if (magic != GgufMagic)
        {
            throw new InvalidDataException($"'{filePath}' n'est pas un fichier GGUF valide (magic incorrect).");
        }

        var version = reader.ReadUInt32();
        var tensorCount = checked((long)reader.ReadUInt64());
        var metadataKvCount = checked((long)reader.ReadUInt64());

        var metadata = new Dictionary<string, object?>();
        for (var i = 0; i < metadataKvCount; i++)
        {
            var key = ReadGgufString(reader);
            var value = ReadGgufValue(reader);
            metadata[key] = value;
        }

        // Un modèle découpé en plusieurs fichiers (gguf-split) ne contient qu'une fraction des
        // tenseurs par fichier : mieux vaut échouer explicitement que produire silencieusement des
        // métadonnées (taille, nombre de couches) qui ne couvrent qu'un seul morceau du modèle.
        var splitCount = (int)(GetUInt(metadata, "split.count") ?? 1);
        if (splitCount > 1)
        {
            throw new InvalidDataException(
                $"'{filePath}' fait partie d'un modèle découpé en {splitCount} fichiers (multi-shard) — " +
                "seul ce fichier a été fourni. Téléchargez tous les fichiers du groupe pour lire les métadonnées complètes.");
        }

        var tensors = new List<(string Name, long[] Dims, int GgmlType, ulong Offset)>((int)Math.Min(tensorCount, int.MaxValue));
        for (var i = 0; i < tensorCount; i++)
        {
            var name = ReadGgufString(reader);
            var nDims = reader.ReadUInt32();
            var dims = new long[nDims];
            for (var d = 0; d < nDims; d++)
            {
                dims[d] = checked((long)reader.ReadUInt64());
            }

            var type = reader.ReadUInt32();
            var offset = reader.ReadUInt64();
            tensors.Add((name, dims, (int)type, offset));
        }

        var architecture = GetString(metadata, "general.architecture") ?? "unknown";
        var blockCount = (int)(GetUInt(metadata, $"{architecture}.block_count") ?? 0);
        var expertCount = (int)(GetUInt(metadata, $"{architecture}.expert_count") ?? 0);
        var expertUsedCount = (int)(GetUInt(metadata, $"{architecture}.expert_used_count") ?? 0);

        var layers = BuildLayers(tensors, blockCount);
        var isMoe = expertCount > 0 || layers.Any(l => l.IsMoeLayer);

        var nonLayerTensorsSizeBytes = tensors
            .Where(t => !LayerIndexPattern.IsMatch(t.Name))
            .Sum(t => GgmlTypeTraits.ComputeSizeBytes(t.GgmlType, ElementCount(t.Dims)));

        var parameterCount = tensors.Sum(t => ElementCount(t.Dims));

        return new GgufModelMetadata
        {
            Architecture = architecture,
            QuantizationLabel = DominantQuantizationLabel(tensors),
            ParameterCount = parameterCount,
            BlockCount = blockCount,
            EmbeddingLength = (int)(GetUInt(metadata, $"{architecture}.embedding_length") ?? 0),
            ContextLengthTrained = (int)(GetUInt(metadata, $"{architecture}.context_length") ?? 0),
            AttentionHeadCount = (int)(GetUInt(metadata, $"{architecture}.attention.head_count") ?? 0),
            AttentionHeadCountKv = (int)(GetUInt(metadata, $"{architecture}.attention.head_count_kv") ?? 0),
            AttentionKeyLength = (int)(GetUInt(metadata, $"{architecture}.attention.key_length") ?? 0),
            AttentionValueLength = (int)(GetUInt(metadata, $"{architecture}.attention.value_length") ?? 0),
            IsMoe = isMoe,
            ExpertCount = expertCount,
            ExpertUsedCount = expertUsedCount,
            Layers = layers,
            NonLayerTensorsSizeBytes = nonLayerTensorsSizeBytes,
            TotalFileSizeBytes = new FileInfo(filePath).Length,
        };
    }

    private static List<GgufLayerInfo> BuildLayers(
        List<(string Name, long[] Dims, int GgmlType, ulong Offset)> tensors, int blockCount)
    {
        var byLayer = new Dictionary<int, (long Attn, long DenseFfn, long MoeExperts)>();

        foreach (var tensor in tensors)
        {
            var match = LayerIndexPattern.Match(tensor.Name);
            if (!match.Success)
            {
                continue;
            }

            var index = int.Parse(match.Groups[1].Value);
            var size = GgmlTypeTraits.ComputeSizeBytes(tensor.GgmlType, ElementCount(tensor.Dims));
            var current = byLayer.GetValueOrDefault(index);

            if (tensor.Name.Contains("_exps.", StringComparison.Ordinal))
            {
                current.MoeExperts += size;
            }
            else if (tensor.Name.Contains(".ffn_", StringComparison.Ordinal))
            {
                current.DenseFfn += size;
            }
            else
            {
                current.Attn += size;
            }

            byLayer[index] = current;
        }

        var layerCount = Math.Max(blockCount, byLayer.Count == 0 ? 0 : byLayer.Keys.Max() + 1);
        var layers = new List<GgufLayerInfo>(layerCount);
        for (var i = 0; i < layerCount; i++)
        {
            var (attn, denseFfn, moeExperts) = byLayer.GetValueOrDefault(i);
            layers.Add(new GgufLayerInfo
            {
                Index = i,
                IsMoeLayer = moeExperts > 0,
                AttentionSizeBytes = attn,
                DenseFfnSizeBytes = denseFfn,
                MoeExpertsSizeBytes = moeExperts,
            });
        }

        return layers;
    }

    private static long ElementCount(long[] dims)
    {
        long count = 1;
        foreach (var d in dims)
        {
            count *= d;
        }

        return count;
    }

    private static string DominantQuantizationLabel(List<(string Name, long[] Dims, int GgmlType, ulong Offset)> tensors)
    {
        // Le type le plus fréquent parmi les plus gros tenseurs (ffn/attn) reflète mieux la
        // quantification "annoncée" du modèle que les tenseurs F32 (normes, biais) toujours présents.
        var weightTensors = tensors.Where(t => t.Name.EndsWith(".weight", StringComparison.Ordinal) && ElementCount(t.Dims) > 1024);
        var dominant = weightTensors
            .GroupBy(t => t.GgmlType)
            .OrderByDescending(g => g.Sum(t => ElementCount(t.Dims)))
            .Select(g => g.Key)
            .FirstOrDefault();

        return GgmlTypeTraits.LabelFor(dominant);
    }

    private static string ReadGgufString(BinaryReader reader)
    {
        var length = checked((long)reader.ReadUInt64());
        var bytes = reader.ReadBytes(checked((int)length));
        return Encoding.UTF8.GetString(bytes);
    }

    private static object? ReadGgufValue(BinaryReader reader)
    {
        var type = reader.ReadUInt32();
        return ReadGgufValueOfType(reader, type);
    }

    private static object? ReadGgufValueOfType(BinaryReader reader, uint type) => type switch
    {
        0 => reader.ReadByte(),                    // UINT8
        1 => reader.ReadSByte(),                    // INT8
        2 => reader.ReadUInt16(),                   // UINT16
        3 => reader.ReadInt16(),                    // INT16
        4 => reader.ReadUInt32(),                   // UINT32
        5 => reader.ReadInt32(),                    // INT32
        6 => reader.ReadSingle(),                   // FLOAT32
        7 => reader.ReadBoolean(),                  // BOOL
        8 => ReadGgufString(reader),                // STRING
        9 => ReadGgufArray(reader),                  // ARRAY
        10 => reader.ReadUInt64(),                  // UINT64
        11 => reader.ReadInt64(),                   // INT64
        12 => reader.ReadDouble(),                  // FLOAT64
        _ => throw new InvalidDataException($"Type de métadonnée GGUF inconnu : {type}"),
    };

    private static List<object?> ReadGgufArray(BinaryReader reader)
    {
        var elementType = reader.ReadUInt32();
        var length = checked((long)reader.ReadUInt64());
        var items = new List<object?>(checked((int)Math.Min(length, 1_000_000)));
        for (var i = 0; i < length; i++)
        {
            items.Add(ReadGgufValueOfType(reader, elementType));
        }

        return items;
    }

    private static string? GetString(Dictionary<string, object?> metadata, string key) =>
        metadata.TryGetValue(key, out var value) ? value as string : null;

    private static ulong? GetUInt(Dictionary<string, object?> metadata, string key)
    {
        if (!metadata.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            byte b => b,
            ushort us => us,
            uint ui => ui,
            ulong ul => ul,
            sbyte sb when sb >= 0 => (ulong)sb,
            short s when s >= 0 => (ulong)s,
            int i when i >= 0 => (ulong)i,
            long l when l >= 0 => (ulong)l,
            _ => null,
        };
    }
}
