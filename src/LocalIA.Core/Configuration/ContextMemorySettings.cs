using CommunityToolkit.Mvvm.ComponentModel;

namespace LocalIA.Core.Configuration;

public sealed partial class ContextMemorySettings : ObservableObject
{
    [property: EngineFlag(OllamaParameter = "num_ctx", LlamaCppFlag = "--ctx-size", DisplayName = "Taille du contexte")]
    [ObservableProperty]
    private int? contextSize;

    [property: EngineFlag(OllamaParameter = "num_batch", LlamaCppFlag = "--batch-size", DisplayName = "Taille de batch")]
    [ObservableProperty]
    private int? batchSize;

    [property: EngineFlag(LlamaCppFlag = "--ubatch-size", DisplayName = "Taille de micro-batch")]
    [ObservableProperty]
    private int? ubatchSize;

    [property: EngineFlag(OllamaParameter = "num_keep", LlamaCppFlag = "--keep", DisplayName = "Tokens à conserver du prompt initial")]
    [ObservableProperty]
    private int? numKeep;

    [property: EngineFlag(LlamaCppFlag = "--cache-type-k", DisplayName = "Type de cache K")]
    [ObservableProperty]
    private CacheQuantType? cacheTypeK;

    [property: EngineFlag(LlamaCppFlag = "--cache-type-v", DisplayName = "Type de cache V")]
    [ObservableProperty]
    private CacheQuantType? cacheTypeV;

    [property: EngineFlag(LlamaCppFlag = "--kv-offload", DisplayName = "Décharger le cache KV sur GPU")]
    [ObservableProperty]
    private bool? kvOffload;

    [property: EngineFlag(LlamaCppFlag = "-lm", DisplayName = "Mode de chargement mémoire")]
    [ObservableProperty]
    private MemoryLoadMode? loadMode;

    [property: EngineFlag(OllamaParameter = "numa", LlamaCppFlag = "--numa", DisplayName = "Optimisations NUMA")]
    [ObservableProperty]
    private NumaPolicy? numaPolicy;

    [property: EngineFlag(LlamaCppFlag = "--flash-attn", DisplayName = "Flash Attention")]
    [ObservableProperty]
    private FlashAttentionMode? flashAttention;

    [property: EngineFlag(LlamaCppFlag = "--rope-scaling", DisplayName = "Type de mise à l'échelle RoPE")]
    [ObservableProperty]
    private RopeScalingType? ropeScalingType;

    [property: EngineFlag(LlamaCppFlag = "--rope-scale", DisplayName = "Facteur d'échelle RoPE")]
    [ObservableProperty]
    private double? ropeScale;

    [property: EngineFlag(LlamaCppFlag = "--rope-freq-base", DisplayName = "RoPE fréquence de base")]
    [ObservableProperty]
    private double? ropeFreqBase;

    [property: EngineFlag(LlamaCppFlag = "--rope-freq-scale", DisplayName = "RoPE fréquence d'échelle")]
    [ObservableProperty]
    private double? ropeFreqScale;

    [property: EngineFlag(LlamaCppFlag = "--context-shift", DisplayName = "Décalage de contexte (génération infinie)")]
    [ObservableProperty]
    private bool? contextShift;
}
