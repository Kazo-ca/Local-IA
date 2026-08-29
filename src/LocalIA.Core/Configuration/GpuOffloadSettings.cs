using CommunityToolkit.Mvvm.ComponentModel;

namespace LocalIA.Core.Configuration;

public sealed partial class GpuOffloadSettings : ObservableObject
{
    [property: EngineFlag(OllamaParameter = "num_gpu", LlamaCppFlag = "-ngl", DisplayName = "Couches GPU")]
    [ObservableProperty]
    private GpuLayerSpec gpuLayers = new();

    [property: EngineFlag(OllamaParameter = "main_gpu", LlamaCppFlag = "--main-gpu", DisplayName = "GPU principal (index)")]
    [ObservableProperty]
    private int? mainGpu;

    [property: EngineFlag(LlamaCppFlag = "--split-mode", DisplayName = "Mode de répartition multi-GPU")]
    [ObservableProperty]
    private SplitMode? splitMode;

    [property: EngineFlag(LlamaCppFlag = "--tensor-split", DisplayName = "Répartition entre GPU (ex. 3,1)")]
    [ObservableProperty]
    private string? tensorSplit;

    [property: EngineFlag(LlamaCppFlag = "--device", DisplayName = "Périphérique(s) d'offload")]
    [ObservableProperty]
    private string? device;

    [property: EngineFlag(LlamaCppFlag = "--fit", DisplayName = "Ajustement automatique à la VRAM (--fit)")]
    [ObservableProperty]
    private bool? fit;

    [property: EngineFlag(LlamaCppFlag = "--fit-target", DisplayName = "Marge de sécurité --fit (MiB)")]
    [ObservableProperty]
    private int? fitTargetMiB;

    [property: EngineFlag(LlamaCppFlag = "--fit-ctx", DisplayName = "Contexte minimum --fit")]
    [ObservableProperty]
    private int? fitCtx;

    [property: EngineFlag(OllamaParameter = "low_vram", DisplayName = "Mode VRAM faible (Ollama)")]
    [ObservableProperty]
    private bool? lowVram;

    [property: EngineFlag(OllamaParameter = "num_thread", LlamaCppFlag = "-t", DisplayName = "Threads CPU")]
    [ObservableProperty]
    private int? threads;

    [property: EngineFlag(LlamaCppFlag = "-tb", DisplayName = "Threads CPU (batch)")]
    [ObservableProperty]
    private int? threadsBatch;
}
