using CommunityToolkit.Mvvm.ComponentModel;

namespace LocalIA.Core.Configuration;

// Le long tail des réglages fins --spec-ngram-* n'est délibérément pas modélisé champ par champ
// (dizaines de flags de tuning avancé) : SpecType couvre le choix de stratégie, et l'échappatoire
// RawOverrides.ExtraLlamaCppArgs permet d'ajouter n'importe quel réglage fin non couvert ici.
public sealed partial class SpeculativeDecodingSettings : ObservableObject
{
    [property: EngineFlag(LlamaCppFlag = "--spec-type", DisplayName = "Stratégie de décodage spéculatif")]
    [ObservableProperty]
    private SpeculativeType? type;

    [property: EngineFlag(LlamaCppFlag = "--spec-draft-model", DisplayName = "Modèle brouillon (draft)")]
    [ObservableProperty]
    private string? draftModelPath;

    [property: EngineFlag(LlamaCppFlag = "--spec-draft-hf", DisplayName = "Modèle brouillon (dépôt Hugging Face)")]
    [ObservableProperty]
    private string? draftHfRepo;

    [property: EngineFlag(LlamaCppFlag = "--spec-draft-n-max", DisplayName = "Tokens brouillon max")]
    [ObservableProperty]
    private int? nMax;

    [property: EngineFlag(LlamaCppFlag = "--spec-draft-n-min", DisplayName = "Tokens brouillon min")]
    [ObservableProperty]
    private int? nMin;

    [property: EngineFlag(LlamaCppFlag = "--spec-draft-device", DisplayName = "Périphérique du modèle brouillon")]
    [ObservableProperty]
    private string? draftDevice;

    [property: EngineFlag(LlamaCppFlag = "--spec-draft-ngl", DisplayName = "Couches GPU du modèle brouillon")]
    [ObservableProperty]
    private int? draftGpuLayers;
}
