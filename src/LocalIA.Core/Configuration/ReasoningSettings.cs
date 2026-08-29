using CommunityToolkit.Mvvm.ComponentModel;

namespace LocalIA.Core.Configuration;

public sealed partial class ReasoningSettings : ObservableObject
{
    [property: EngineFlag(LlamaCppFlag = "--reasoning", DisplayName = "Raisonnement (thinking)")]
    [ObservableProperty]
    private ReasoningMode? mode;

    [property: EngineFlag(LlamaCppFlag = "--reasoning-format", DisplayName = "Format des traces de raisonnement")]
    [ObservableProperty]
    private ReasoningFormat? format;

    [property: EngineFlag(LlamaCppFlag = "--reasoning-effort", DisplayName = "Effort de raisonnement")]
    [ObservableProperty]
    private string? effort;

    [property: EngineFlag(LlamaCppFlag = "--reasoning-budget", DisplayName = "Budget de tokens de raisonnement")]
    [ObservableProperty]
    private int? budget;

    [property: EngineFlag(LlamaCppFlag = "--reasoning-preserve", DisplayName = "Conserver le raisonnement dans l'historique")]
    [ObservableProperty]
    private bool? preserve;

    [property: EngineFlag(LlamaCppFlag = "--jinja", DisplayName = "Utiliser le moteur de template Jinja")]
    [ObservableProperty]
    private bool? jinja;

    [property: EngineFlag(LlamaCppFlag = "--prefill-assistant", DisplayName = "Pré-remplir la réponse de l'assistant")]
    [ObservableProperty]
    private bool? prefillAssistant;
}
