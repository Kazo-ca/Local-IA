using CommunityToolkit.Mvvm.ComponentModel;

namespace LocalIA.Core.Configuration;

public sealed partial class MultimodalSettings : ObservableObject
{
    [property: EngineFlag(LlamaCppFlag = "--mmproj", DisplayName = "Projecteur multimodal (fichier)")]
    [ObservableProperty]
    private string? mmprojPath;

    [property: EngineFlag(LlamaCppFlag = "--mmproj-auto", DisplayName = "Projecteur multimodal automatique")]
    [ObservableProperty]
    private bool? mmprojAuto;

    [property: EngineFlag(LlamaCppFlag = "--mmproj-offload", DisplayName = "Décharger le projecteur sur GPU")]
    [ObservableProperty]
    private bool? mmprojOffload;

    [property: EngineFlag(LlamaCppFlag = "--image-min-tokens", DisplayName = "Tokens image minimum")]
    [ObservableProperty]
    private int? imageMinTokens;

    [property: EngineFlag(LlamaCppFlag = "--image-max-tokens", DisplayName = "Tokens image maximum")]
    [ObservableProperty]
    private int? imageMaxTokens;

    [property: EngineFlag(LlamaCppFlag = "--embedding", DisplayName = "Mode embeddings uniquement")]
    [ObservableProperty]
    private bool? embeddingMode;

    [property: EngineFlag(LlamaCppFlag = "--rerank", DisplayName = "Activer le reranking")]
    [ObservableProperty]
    private bool? rerankingMode;

    [property: EngineFlag(LlamaCppFlag = "--pooling", DisplayName = "Type de pooling")]
    [ObservableProperty]
    private PoolingType? pooling;
}
