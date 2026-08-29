namespace LocalIA.Core.Gguf;

public sealed class GgufLayerInfo
{
    public required int Index { get; init; }
    public bool IsMoeLayer { get; init; }
    public long AttentionSizeBytes { get; init; }
    public long DenseFfnSizeBytes { get; init; }
    public long MoeExpertsSizeBytes { get; init; }

    public long TotalSizeBytes => AttentionSizeBytes + DenseFfnSizeBytes + MoeExpertsSizeBytes;
}

public sealed class GgufModelMetadata
{
    public string Architecture { get; init; } = "";
    public string QuantizationLabel { get; init; } = "";
    public long ParameterCount { get; init; }
    public int BlockCount { get; init; }
    public int EmbeddingLength { get; init; }
    public int ContextLengthTrained { get; init; }
    public int AttentionHeadCount { get; init; }
    public int AttentionHeadCountKv { get; init; }

    /// <summary>
    /// Dimension d'une tête d'attention (clé), lue directement depuis {arch}.attention.key_length
    /// quand présente. Certaines architectures (ex. Gemma) fixent le head_dim indépendamment de
    /// EmbeddingLength/AttentionHeadCount — leur simple division ne donne alors pas la vraie valeur.
    /// 0 si absente des métadonnées (l'appelant retombe alors sur EmbeddingLength/AttentionHeadCount).
    /// </summary>
    public int AttentionKeyLength { get; init; }

    /// <summary>Idem pour la valeur (V), depuis {arch}.attention.value_length.</summary>
    public int AttentionValueLength { get; init; }

    public bool IsMoe { get; init; }
    public int ExpertCount { get; init; }
    public int ExpertUsedCount { get; init; }
    public IReadOnlyList<GgufLayerInfo> Layers { get; init; } = [];
    public long NonLayerTensorsSizeBytes { get; init; }
    public long TotalFileSizeBytes { get; init; }

    public IEnumerable<GgufLayerInfo> MoeLayers => Layers.Where(l => l.IsMoeLayer);
}
