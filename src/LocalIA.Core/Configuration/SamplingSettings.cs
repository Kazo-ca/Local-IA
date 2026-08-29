using CommunityToolkit.Mvvm.ComponentModel;

namespace LocalIA.Core.Configuration;

public sealed partial class SamplingSettings : ObservableObject
{
    [property: EngineFlag(OllamaParameter = "temperature", LlamaCppFlag = "--temp", DisplayName = "Température")]
    [ObservableProperty]
    private double? temperature;

    [property: EngineFlag(OllamaParameter = "top_k", LlamaCppFlag = "--top-k", DisplayName = "Top K")]
    [ObservableProperty]
    private int? topK;

    [property: EngineFlag(OllamaParameter = "top_p", LlamaCppFlag = "--top-p", DisplayName = "Top P")]
    [ObservableProperty]
    private double? topP;

    [property: EngineFlag(OllamaParameter = "min_p", LlamaCppFlag = "--min-p", DisplayName = "Min P")]
    [ObservableProperty]
    private double? minP;

    [property: EngineFlag(LlamaCppFlag = "--top-nsigma", DisplayName = "Top-N-Sigma")]
    [ObservableProperty]
    private double? topNSigma;

    [property: EngineFlag(OllamaParameter = "seed", LlamaCppFlag = "--seed", DisplayName = "Seed (graine aléatoire)")]
    [ObservableProperty]
    private int? seed;

    [property: EngineFlag(OllamaParameter = "repeat_last_n", LlamaCppFlag = "--repeat-last-n", DisplayName = "Repeat last N")]
    [ObservableProperty]
    private int? repeatLastN;

    [property: EngineFlag(OllamaParameter = "repeat_penalty", LlamaCppFlag = "--repeat-penalty", DisplayName = "Repeat penalty")]
    [ObservableProperty]
    private double? repeatPenalty;

    [property: EngineFlag(LlamaCppFlag = "--presence-penalty", DisplayName = "Presence penalty")]
    [ObservableProperty]
    private double? presencePenalty;

    [property: EngineFlag(LlamaCppFlag = "--frequency-penalty", DisplayName = "Frequency penalty")]
    [ObservableProperty]
    private double? frequencyPenalty;

    [property: EngineFlag(OllamaParameter = "penalize_newline", DisplayName = "Pénaliser les retours à la ligne")]
    [ObservableProperty]
    private bool? penalizeNewline;

    [property: EngineFlag(OllamaParameter = "typical_p", LlamaCppFlag = "--typical", DisplayName = "Typical P")]
    [ObservableProperty]
    private double? typicalP;

    [property: EngineFlag(LlamaCppFlag = "--xtc-probability", DisplayName = "XTC probabilité")]
    [ObservableProperty]
    private double? xtcProbability;

    [property: EngineFlag(LlamaCppFlag = "--xtc-threshold", DisplayName = "XTC seuil")]
    [ObservableProperty]
    private double? xtcThreshold;

    [property: EngineFlag(LlamaCppFlag = "--dry-multiplier", DisplayName = "DRY multiplicateur")]
    [ObservableProperty]
    private double? dryMultiplier;

    [property: EngineFlag(LlamaCppFlag = "--dry-base", DisplayName = "DRY base")]
    [ObservableProperty]
    private double? dryBase;

    [property: EngineFlag(LlamaCppFlag = "--dry-allowed-length", DisplayName = "DRY longueur autorisée")]
    [ObservableProperty]
    private int? dryAllowedLength;

    [property: EngineFlag(LlamaCppFlag = "--dry-penalty-last-n", DisplayName = "DRY pénalité sur les N derniers")]
    [ObservableProperty]
    private int? dryPenaltyLastN;

    [property: EngineFlag(LlamaCppFlag = "--dynatemp-range", DisplayName = "Dynatemp plage")]
    [ObservableProperty]
    private double? dynatempRange;

    [property: EngineFlag(LlamaCppFlag = "--dynatemp-exp", DisplayName = "Dynatemp exposant")]
    [ObservableProperty]
    private double? dynatempExp;

    [property: EngineFlag(OllamaParameter = "mirostat", LlamaCppFlag = "--mirostat", DisplayName = "Mirostat (0/1/2)")]
    [ObservableProperty]
    private int? mirostat;

    [property: EngineFlag(OllamaParameter = "mirostat_eta", LlamaCppFlag = "--mirostat-lr", DisplayName = "Mirostat eta")]
    [ObservableProperty]
    private double? mirostatEta;

    [property: EngineFlag(OllamaParameter = "mirostat_tau", LlamaCppFlag = "--mirostat-ent", DisplayName = "Mirostat tau")]
    [ObservableProperty]
    private double? mirostatTau;

    [property: EngineFlag(OllamaParameter = "stop", LlamaCppFlag = "--reverse-prompt", DisplayName = "Séquences d'arrêt (une par ligne)")]
    [ObservableProperty]
    private string? stopSequences;

    [property: EngineFlag(LlamaCppFlag = "--grammar", DisplayName = "Grammaire GBNF")]
    [ObservableProperty]
    private string? grammar;

    [property: EngineFlag(LlamaCppFlag = "--json-schema", DisplayName = "Schéma JSON")]
    [ObservableProperty]
    private string? jsonSchema;
}
