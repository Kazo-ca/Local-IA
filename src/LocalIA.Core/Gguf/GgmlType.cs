namespace LocalIA.Core.Gguf;

/// <summary>
/// Identifiants numériques ggml_type tels qu'écrits dans les fichiers GGUF (stables depuis les
/// premières versions du format — ne jamais réordonner). Types expérimentaux ultérieurs non
/// listés ici tombent sur le cas "inconnu" du lecteur plutôt que de planter.
/// </summary>
public enum GgmlType
{
    F32 = 0,
    F16 = 1,
    Q4_0 = 2,
    Q4_1 = 3,
    Q5_0 = 6,
    Q5_1 = 7,
    Q8_0 = 8,
    Q8_1 = 9,
    Q2_K = 10,
    Q3_K = 11,
    Q4_K = 12,
    Q5_K = 13,
    Q6_K = 14,
    Q8_K = 15,
    Iq2Xxs = 16,
    Iq2Xs = 17,
    Iq3Xxs = 18,
    Iq1S = 19,
    Iq4Nl = 20,
    Iq3S = 21,
    Iq2S = 22,
    Iq4Xs = 23,
    I8 = 24,
    I16 = 25,
    I32 = 26,
    I64 = 27,
    F64 = 28,
    Iq1M = 29,
    Bf16 = 30,
    Tq1_0 = 34,
    Tq2_0 = 35,
}

/// <summary>
/// (BlockSize en éléments, TypeSizeBytes par bloc) — valeurs issues de ggml (ggml.c
/// type_traits[]). Validées empiriquement en phase 6 contre un vrai fichier GGUF (les deltas
/// d'offset entre tenseurs consécutifs permettent de vérifier chaque taille calculée).
/// </summary>
public static class GgmlTypeTraits
{
    private static readonly Dictionary<GgmlType, (int BlockSize, int TypeSizeBytes)> Table = new()
    {
        [GgmlType.F32] = (1, 4),
        [GgmlType.F16] = (1, 2),
        [GgmlType.Q4_0] = (32, 18),
        [GgmlType.Q4_1] = (32, 20),
        [GgmlType.Q5_0] = (32, 22),
        [GgmlType.Q5_1] = (32, 24),
        [GgmlType.Q8_0] = (32, 34),
        [GgmlType.Q8_1] = (32, 36),
        [GgmlType.Q2_K] = (256, 84),
        [GgmlType.Q3_K] = (256, 110),
        [GgmlType.Q4_K] = (256, 144),
        [GgmlType.Q5_K] = (256, 176),
        [GgmlType.Q6_K] = (256, 210),
        [GgmlType.Q8_K] = (256, 292),
        [GgmlType.Iq2Xxs] = (256, 66),
        [GgmlType.Iq2Xs] = (256, 74),
        [GgmlType.Iq3Xxs] = (256, 98),
        [GgmlType.Iq1S] = (256, 50),
        [GgmlType.Iq4Nl] = (32, 18),
        [GgmlType.Iq3S] = (256, 110),
        [GgmlType.Iq2S] = (256, 82),
        [GgmlType.Iq4Xs] = (256, 136),
        [GgmlType.I8] = (1, 1),
        [GgmlType.I16] = (1, 2),
        [GgmlType.I32] = (1, 4),
        [GgmlType.I64] = (1, 8),
        [GgmlType.F64] = (1, 8),
        [GgmlType.Iq1M] = (256, 56),
        [GgmlType.Bf16] = (1, 2),
        [GgmlType.Tq1_0] = (256, 54),
        [GgmlType.Tq2_0] = (256, 66),
    };

    public static bool TryGetTraits(int rawTypeId, out int blockSize, out int typeSizeBytes)
    {
        if (Enum.IsDefined(typeof(GgmlType), rawTypeId) && Table.TryGetValue((GgmlType)rawTypeId, out var traits))
        {
            (blockSize, typeSizeBytes) = traits;
            return true;
        }

        blockSize = 0;
        typeSizeBytes = 0;
        return false;
    }

    public static long ComputeSizeBytes(int rawTypeId, long elementCount)
    {
        if (!TryGetTraits(rawTypeId, out var blockSize, out var typeSizeBytes))
        {
            return 0;
        }

        return elementCount / blockSize * typeSizeBytes;
    }

    public static string LabelFor(int rawTypeId) =>
        Enum.IsDefined(typeof(GgmlType), rawTypeId) ? ((GgmlType)rawTypeId).ToString() : $"type{rawTypeId}";
}
