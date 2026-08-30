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

    /// <summary>Levé après application d'une recommandation aux réglages du palier —
    /// ModelConfigurationViewModel s'y abonne pour rafraîchir ses onglets et marquer IsDirty.</summary>
    public event Action? RecommendationApplied;

    partial void OnRecommendationChanged(AdvisorRecommendation? value) => OnPropertyChanged(nameof(RecommendedParamsText));

    /// <summary>Résumé des valeurs concrètes que "Appliquer" écrirait dans le palier — null tant
    /// qu'aucune évaluation n'a encore eu lieu.</summary>
    public string? RecommendedParamsText
    {
        get
        {
            if (Recommendation is not { } rec)
            {
                return null;
            }

            var parts = new List<string> { $"Contexte : {rec.RecommendedContextSize}" };
            if (rec.MoeRecommendation is { } moe)
            {
                parts.Add($"Experts sur CPU : {moe.RecommendedNCpuMoe}/{moe.TotalMoeLayers}");
            }
            else if (rec.RecommendedGpuLayers is { } gpuLayers)
            {
                parts.Add($"Couches GPU : {gpuLayers}");
            }

            return string.Join("   ·   ", parts);
        }
    }

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

    [RelayCommand]
    private void ApplyRecommendation()
    {
        if (Recommendation is not { } rec || _tier is null)
        {
            return;
        }

        _tier.Settings.ContextMemory.ContextSize = rec.RecommendedContextSize;

        if (rec.MoeRecommendation is { } moe)
        {
            // Mêmes règles que MoeViewModel.ApplyRecommendation : les N premières couches d'experts
            // (par index croissant, déjà l'ordre de MoeLayersInOrder) passent en RAM CPU.
            _tier.Settings.MoeOffload.CpuLayerIndices = moe.MoeLayersInOrder
                .Take(moe.RecommendedNCpuMoe)
                .Select(l => l.Index)
                .ToHashSet();
            _tier.Settings.ApplyRecommendedLoadModeIfUnset();
        }
        else if (rec.RecommendedGpuLayers is { } gpuLayers)
        {
            _tier.Settings.GpuOffload.GpuLayers = new GpuLayerSpec { Mode = GpuLayerMode.Explicit, ExplicitCount = gpuLayers };
        }

        StatusMessage = "Recommandation appliquée aux réglages du palier — pense à Enregistrer.";
        RecommendationApplied?.Invoke();
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
            var availableVramBytes = Math.Max(0, hardware.TotalVramBytes - hardware.UsedVramBytes);
            var availableRamBytes = Math.Max(0, hardware.TotalRamBytes - ConfigurationAdvisor.SystemRamReserveBytes);
            var hardwareSummary =
                $"GPU {hardware.GpuName}, VRAM {FormatGiB(hardware.TotalVramBytes)} (utilisée : {FormatGiB(hardware.UsedVramBytes)}, disponible : {FormatGiB(availableVramBytes)}). " +
                $"RAM {FormatGiB(hardware.TotalRamBytes)} au total ({FormatGiB(ConfigurationAdvisor.SystemRamReserveBytes)} réservés au système, {FormatGiB(availableRamBytes)} disponibles pour l'IA en permanence, indépendamment de l'utilisation RAM constatée à l'instant présent).";
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
