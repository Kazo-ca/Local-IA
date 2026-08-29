using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalIA.Core.Abstractions;
using LocalIA.Core.Advisor;
using LocalIA.Core.Configuration;
using LocalIA.Core.Gguf;
using LocalIA.Core.HuggingFace;
using LocalIA.Core.Models;

namespace LocalIA.App.ViewModels;

public sealed partial class ConfigurationAdvisorViewModel : ObservableObject
{
    private readonly IConfigurationAdvisor _advisor;
    private readonly IGgufMetadataReader _ggufReader;
    private readonly IHardwareMonitorService _hardwareMonitor;
    private readonly IHuggingFaceClient _huggingFaceClient;
    private readonly IOllamaApiClient _ollamaApiClient;

    private ModelTier? _tier;

    [ObservableProperty]
    private AdvisorRecommendation? recommendation;

    [ObservableProperty]
    private bool isEvaluating;

    [ObservableProperty]
    private bool isAskingAi;

    [ObservableProperty]
    private string? aiAgreement;

    [ObservableProperty]
    private string? aiRiskLevel;

    [ObservableProperty]
    private string? aiJustification;

    [ObservableProperty]
    private string? aiAlternativeSuggestion;

    [ObservableProperty]
    private string? statusMessage;

    [ObservableProperty]
    private string? selectedAdvisorModel;

    public ObservableCollection<string> AvailableAdvisorModels { get; } = [];

    public ConfigurationAdvisorViewModel(
        IConfigurationAdvisor advisor,
        IGgufMetadataReader ggufReader,
        IHardwareMonitorService hardwareMonitor,
        IHuggingFaceClient huggingFaceClient,
        IOllamaApiClient ollamaApiClient)
    {
        _advisor = advisor;
        _ggufReader = ggufReader;
        _hardwareMonitor = hardwareMonitor;
        _huggingFaceClient = huggingFaceClient;
        _ollamaApiClient = ollamaApiClient;
    }

    public void LoadTier(ModelTier tier)
    {
        _tier = tier;
        AiAgreement = null;
        AiJustification = null;
        AiRiskLevel = null;
        AiAlternativeSuggestion = null;
        // Passe par la commande générée : AsyncRelayCommand fait remonter une exception jusqu'au
        // Dispatcher WPF (filet de sécurité global) au lieu de la laisser disparaître silencieusement.
        _ = EvaluateCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task EvaluateAsync()
    {
        if (_tier is null)
        {
            return;
        }

        IsEvaluating = true;
        StatusMessage = null;
        try
        {
            var facts = await BuildCandidateFactsAsync(_tier);
            var hardware = _hardwareMonitor.Current;
            var desiredContext = _tier.Settings.ContextMemory.ContextSize ?? 4096;
            Recommendation = _advisor.Evaluate(hardware, facts, desiredContext);
        }
        finally
        {
            IsEvaluating = false;
        }
    }

    private async Task<CandidateModelFacts> BuildCandidateFactsAsync(ModelTier tier)
    {
        GgufModelMetadata? metadata = null;
        var localPath = tier.LlamaCppSource?.LocalFilePath;
        if (!string.IsNullOrWhiteSpace(localPath) && File.Exists(localPath))
        {
            try
            {
                metadata = _ggufReader.Read(localPath);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException)
            {
                StatusMessage = $"Lecture des métadonnées impossible : {ex.Message}";
            }
        }

        var hasMmproj = false;
        var hasDraft = false;
        if (tier.LlamaCppSource?.HfRepoId is { } repoId)
        {
            try
            {
                var files = await _huggingFaceClient.ListRepoFilesAsync(repoId);
                hasMmproj = files.Any(f => f.Role == GgufFileRole.MultimodalProjector);
                hasDraft = files.Any(f => f.Role == GgufFileRole.DraftModel);
            }
            catch (HttpRequestException)
            {
                // Non bloquant : l'avis se construit simplement sans mention des fichiers complémentaires.
            }
        }

        return new CandidateModelFacts
        {
            DisplayName = tier.Label,
            FileSizeBytes = metadata?.TotalFileSizeBytes ?? 0,
            QuantizationLabel = metadata?.QuantizationLabel ?? tier.LlamaCppSource?.QuantHint,
            GgufMetadata = metadata,
            HasMultimodalProjector = hasMmproj,
            HasDraftModel = hasDraft,
            CacheTypeK = tier.Settings.ContextMemory.CacheTypeK ?? CacheQuantType.F16,
            CacheTypeV = tier.Settings.ContextMemory.CacheTypeV ?? CacheQuantType.F16,
        };
    }

    [RelayCommand]
    private async Task AskAiAsync()
    {
        if (Recommendation is null || _tier is null)
        {
            return;
        }

        IsAskingAi = true;
        StatusMessage = null;
        AiJustification = null;
        try
        {
            await RefreshAvailableModelsAsync();
            var modelName = SelectedAdvisorModel ?? AvailableAdvisorModels.FirstOrDefault();
            if (modelName is null)
            {
                StatusMessage = "Aucun modèle Ollama installé pour demander un avis (démarrez Ollama et installez au moins un modèle).";
                return;
            }

            var hardware = _hardwareMonitor.Current;
            var hardwareSummary = $"GPU {hardware.GpuName}, VRAM {FormatGiB(hardware.TotalVramBytes)} (utilisée : {FormatGiB(hardware.UsedVramBytes)}), RAM {FormatGiB(hardware.TotalRamBytes)}.";
            var modelKind = Recommendation.MoeRecommendation is not null ? "MoE" : "dense";
            var modelSummary = $"{_tier.Label} ({modelKind}, quantification {_tier.LlamaCppSource?.QuantHint ?? "inconnue"}).";
            var verdictText = $"{Recommendation.Verdict} — {Recommendation.Summary}";

            var userMessage = AdvisorPromptBuilder.BuildUserMessage(hardwareSummary, modelSummary, verdictText, Recommendation.Notes);
            var request = new ChatRequest
            {
                Model = modelName,
                Messages =
                [
                    new ChatMessage(ChatRole.System, AdvisorPromptBuilder.SystemMessage),
                    new ChatMessage(ChatRole.User, userMessage),
                ],
            };

            string raw;
            try
            {
                raw = await _ollamaApiClient.ChatOnceAsync(request, AdvisorPromptBuilder.JsonSchema);
            }
            catch (HttpRequestException ex)
            {
                StatusMessage = $"Erreur lors de l'appel au modèle « {modelName} » : {ex.Message}";
                return;
            }
            catch (Exception ex) when (ex is TaskCanceledException or TimeoutException)
            {
                // Un chargement à froid du modèle (première requête après le démarrage d'Ollama)
                // peut dépasser le délai configuré : ne jamais laisser cette attente planter l'appli.
                StatusMessage = $"Le modèle « {modelName} » n'a pas répondu à temps (chargement trop long ou modèle bloqué).";
                return;
            }
            catch (JsonException ex)
            {
                StatusMessage = $"Réponse inattendue du modèle « {modelName} » : {ex.Message}";
                return;
            }

            ApplyAiResponse(raw);
        }
        finally
        {
            IsAskingAi = false;
        }
    }

    private void ApplyAiResponse(string raw)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<AiAdvisorStructuredResponse>(raw);
            if (parsed is null || string.IsNullOrWhiteSpace(parsed.Justification))
            {
                throw new JsonException("Réponse structurée vide.");
            }

            AiAgreement = parsed.Agreement;
            AiRiskLevel = parsed.RiskLevel;
            AiJustification = parsed.Justification;
            AiAlternativeSuggestion = parsed.AlternativeSuggestion;
        }
        catch (JsonException)
        {
            // Repli obligatoire en texte libre : un petit modèle local ne respecte pas toujours
            // un schéma strict, la fonctionnalité ne doit jamais échouer pour autant.
            AiAgreement = "unknown";
            AiRiskLevel = null;
            AiAlternativeSuggestion = null;
            AiJustification = raw;
        }
    }

    private async Task RefreshAvailableModelsAsync()
    {
        AvailableAdvisorModels.Clear();
        var tags = await _ollamaApiClient.ListTagsAsync();
        foreach (var tag in tags.OrderBy(t => t.SizeBytes))
        {
            AvailableAdvisorModels.Add(tag.Name);
        }
    }

    private static string FormatGiB(long bytes) => LocalIA.App.Converters.BytesToGigabytesConverter.Format(bytes);
}
