using System.Text.RegularExpressions;

namespace LocalIA.Core.HuggingFace;

/// <summary>
/// Extraction permissive du type de quantification depuis un nom de fichier GGUF. Les
/// quantizeurs (bartowski, TheBloke, unsloth, mradermacher…) ne formatent pas toujours les noms
/// de la même façon — un échec de parsing n'est jamais bloquant, le nom de fichier brut reste
/// toujours affiché à côté.
/// </summary>
public static partial class GgufQuantizationParser
{
    [GeneratedRegex(@"(?:^|[-_.])(IQ\d_[A-Za-z]+|Q\d(?:_[A-Za-z0-9]+)*|BF16|F16|F32)(?:[-_.]|$)", RegexOptions.IgnoreCase)]
    private static partial Regex QuantPattern();

    public static string? TryParse(string fileName)
    {
        if (!fileName.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var match = QuantPattern().Match(fileName);
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
    }

    /// <summary>Retire un éventuel suffixe multi-shard "-00001-of-00005" pour regrouper les fichiers d'un même quant.</summary>
    [GeneratedRegex(@"-\d{5}-of-\d{5}(?=\.gguf$)", RegexOptions.IgnoreCase)]
    private static partial Regex ShardSuffixPattern();

    public static string RemoveShardSuffix(string fileName) => ShardSuffixPattern().Replace(fileName, "");
}
