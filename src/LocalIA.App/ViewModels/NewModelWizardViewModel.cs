using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalIA.Core.Abstractions;
using LocalIA.Core.Advisor;
using LocalIA.Core.Configuration;
using LocalIA.Core.Gguf;
using LocalIA.Core.HuggingFace;
using LocalIA.Core.Models;
using Microsoft.Win32;

namespace LocalIA.App.ViewModels;

public enum WizardStep
{
    Profile,
    Model,
    Configure,
    Summary,
}

/// <summary>
/// Assistant guidé de création de profil/palier en une seule fenêtre : contrairement aux écrans
/// Profils / Configuration du modèle / MoE / Conseiller (qui restent la voie normale pour éditer
/// un palier existant en détail), le wizard enchaîne les 4 étapes sans navigation et applique
/// automatiquement les réglages techniques recommandés. Utilise les services Core directement
/// (pas ConfigurationAdvisorViewModel/MoeViewModel, qui sont des singletons liés à l'écran de
/// navigation principal) : le palier en construction ici est un brouillon local, jamais partagé
/// tant que "Créer" (étape 4) n'a pas été cliqué.
/// </summary>
public sealed partial class NewModelWizardViewModel : ObservableObject
{
    private readonly IAppConfigRepository _configRepository;
    private readonly IHuggingFaceClient _huggingFaceClient;
    private readonly IGgufDownloader _downloader;
    private readonly IOllamaApiClient _ollamaApiClient;
    private readonly IGgufMetadataReader _ggufReader;
    private readonly IConfigurationAdvisor _advisor;
    private readonly IHardwareMonitorService _hardwareMonitor;

    private CancellationTokenSource? _searchDebounceCts;
    private GgufModelMetadata? _metadata;
    private bool _hasMultimodalProjector;
    private bool _hasDraftModel;

    [ObservableProperty]
    private WizardStep step = WizardStep.Profile;

    public bool IsProfileStep => Step == WizardStep.Profile;
    public bool IsModelStep => Step == WizardStep.Model;
    public bool IsConfigureStep => Step == WizardStep.Configure;
    public bool IsSummaryStep => Step == WizardStep.Summary;
    public bool IsLastStep => Step == WizardStep.Summary;

    partial void OnStepChanged(WizardStep value)
    {
        OnPropertyChanged(nameof(IsProfileStep));
        OnPropertyChanged(nameof(IsModelStep));
        OnPropertyChanged(nameof(IsConfigureStep));
        OnPropertyChanged(nameof(IsSummaryStep));
        OnPropertyChanged(nameof(IsLastStep));
    }

    [ObservableProperty]
    private string? statusMessage;

    public bool CreatedSuccessfully { get; private set; }

    // ---------- Étape 1 : Profil ----------

    [ObservableProperty]
    private bool isNewProfile = true;

    [ObservableProperty]
    private string newProfileName = "";

    [ObservableProperty]
    private string newProfileDescription = "";

    [ObservableProperty]
    private ModelProfile? selectedExistingProfile;

    [ObservableProperty]
    private string tierLabel = "";

    public ObservableCollection<ModelProfile> ExistingProfiles { get; } = [];

    public bool CanLeaveProfileStep =>
        (IsNewProfile ? !string.IsNullOrWhiteSpace(NewProfileName) : SelectedExistingProfile is not null)
        && !string.IsNullOrWhiteSpace(TierLabel);

    // ---------- Étape 2 : Modèle ----------

    [ObservableProperty]
    private EngineKind engine = EngineKind.LlamaCpp;

    public bool IsLlamaCppEngineSelected
    {
        get => Engine == EngineKind.LlamaCpp;
        set { if (value) { Engine = EngineKind.LlamaCpp; } }
    }

    public bool IsOllamaEngineSelected
    {
        get => Engine == EngineKind.Ollama;
        set { if (value) { Engine = EngineKind.Ollama; } }
    }

    [ObservableProperty]
    private string? systemPrompt;

    // Ollama
    [ObservableProperty]
    private string ollamaBaseModel = "";

    [ObservableProperty]
    private string ollamaCustomModelName = "";

    public ObservableCollection<string> InstalledOllamaModels { get; } = [];

    // llama.cpp
    [ObservableProperty]
    private bool useLocalFile = true;

    [ObservableProperty]
    private string? localFilePath;

    [ObservableProperty]
    private string searchText = "";

    [ObservableProperty]
    private bool isSearching;

    [ObservableProperty]
    private HfModelSummary? selectedSearchResult;

    [ObservableProperty]
    private bool isLoadingFiles;

    [ObservableProperty]
    private HfFileRowViewModel? selectedFile;

    [ObservableProperty]
    private bool isDownloading;

    public ObservableCollection<HfModelSummary> SearchResults { get; } = [];

    public ObservableCollection<HfFileRowViewModel> RepoFiles { get; } = [];

    public bool CanLeaveModelStep => Engine == EngineKind.Ollama
        ? !string.IsNullOrWhiteSpace(OllamaBaseModel) && !string.IsNullOrWhiteSpace(OllamaCustomModelName)
        : !string.IsNullOrWhiteSpace(LocalFilePath) && File.Exists(LocalFilePath);

    // ---------- Étape 3 : Configuration automatique ----------

    [ObservableProperty]
    private bool isAnalyzing;

    [ObservableProperty]
    private AdvisorRecommendation? recommendation;

    [ObservableProperty]
    private bool isAskingAi;

    [ObservableProperty]
    private string? aiJustification;

    [ObservableProperty]
    private string? aiAgreement;

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

    partial void OnRecommendationChanged(AdvisorRecommendation? value) => OnPropertyChanged(nameof(RecommendedParamsText));

    // ---------- Palier en construction (brouillon, pas encore rattaché à un profil) ----------

    private readonly ModelTier _draftTier = new();

    public NewModelWizardViewModel(
        IAppConfigRepository configRepository,
        IHuggingFaceClient huggingFaceClient,
        IGgufDownloader downloader,
        IOllamaApiClient ollamaApiClient,
        IGgufMetadataReader ggufReader,
        IConfigurationAdvisor advisor,
        IHardwareMonitorService hardwareMonitor)
    {
        _configRepository = configRepository;
        _huggingFaceClient = huggingFaceClient;
        _downloader = downloader;
        _ollamaApiClient = ollamaApiClient;
        _ggufReader = ggufReader;
        _advisor = advisor;
        _hardwareMonitor = hardwareMonitor;

        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        var config = await _configRepository.LoadAsync();
        foreach (var profile in config.Profiles)
        {
            ExistingProfiles.Add(profile);
        }

        SelectedExistingProfile = ExistingProfiles.FirstOrDefault();

        try
        {
            var tags = await _ollamaApiClient.ListTagsAsync();
            foreach (var tag in tags.OrderBy(t => t.Name))
            {
                InstalledOllamaModels.Add(tag.Name);
            }
        }
        catch (HttpRequestException)
        {
            // Ollama non démarré : la liste reste vide, l'utilisateur peut toujours taper un tag à la main.
        }
    }

    [RelayCommand]
    private void GoNext()
    {
        Step = Step switch
        {
            WizardStep.Profile when CanLeaveProfileStep => WizardStep.Model,
            WizardStep.Model when CanLeaveModelStep => WizardStep.Configure,
            WizardStep.Configure => WizardStep.Summary,
            _ => Step,
        };

        if (Step == WizardStep.Configure && Recommendation is null)
        {
            _ = AnalyzeAsync();
        }
    }

    [RelayCommand]
    private void GoBack()
    {
        Step = Step switch
        {
            WizardStep.Model => WizardStep.Profile,
            WizardStep.Configure => WizardStep.Model,
            WizardStep.Summary => WizardStep.Configure,
            _ => Step,
        };
    }

    partial void OnEngineChanged(EngineKind value)
    {
        OnPropertyChanged(nameof(CanLeaveModelStep));
        OnPropertyChanged(nameof(IsLlamaCppEngineSelected));
        OnPropertyChanged(nameof(IsOllamaEngineSelected));
    }
    partial void OnOllamaBaseModelChanged(string value) => OnPropertyChanged(nameof(CanLeaveModelStep));
    partial void OnOllamaCustomModelNameChanged(string value) => OnPropertyChanged(nameof(CanLeaveModelStep));
    partial void OnLocalFilePathChanged(string? value) => OnPropertyChanged(nameof(CanLeaveModelStep));
    partial void OnIsNewProfileChanged(bool value) => OnPropertyChanged(nameof(CanLeaveProfileStep));
    partial void OnNewProfileNameChanged(string value) => OnPropertyChanged(nameof(CanLeaveProfileStep));
    partial void OnSelectedExistingProfileChanged(ModelProfile? value) => OnPropertyChanged(nameof(CanLeaveProfileStep));
    partial void OnTierLabelChanged(string value) => OnPropertyChanged(nameof(CanLeaveProfileStep));

    [RelayCommand]
    private void BrowseLocalFile()
    {
        var dialog = new OpenFileDialog { Filter = "Modèles GGUF (*.gguf)|*.gguf|Tous les fichiers (*.*)|*.*" };
        if (dialog.ShowDialog() == true)
        {
            LocalFilePath = dialog.FileName;
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        _searchDebounceCts?.Cancel();
        var cts = new CancellationTokenSource();
        _searchDebounceCts = cts;
        _ = RunDebouncedSearchAsync(cts);
    }

    private async Task RunDebouncedSearchAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(400, cts.Token);
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                SearchResults.Clear();
                return;
            }

            IsSearching = true;
            var results = await _huggingFaceClient.SearchModelsAsync(SearchText, limit: 20, cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            SearchResults.Clear();
            foreach (var result in results)
            {
                SearchResults.Add(result);
            }
        }
        catch (OperationCanceledException)
        {
            // Une saisie plus récente a déjà annulé cette recherche.
        }
        finally
        {
            IsSearching = false;
        }
    }

    partial void OnSelectedSearchResultChanged(HfModelSummary? value) => _ = LoadRepoFilesAsync();

    private async Task LoadRepoFilesAsync()
    {
        RepoFiles.Clear();
        if (SelectedSearchResult is null)
        {
            return;
        }

        IsLoadingFiles = true;
        try
        {
            var files = await _huggingFaceClient.ListRepoFilesAsync(SelectedSearchResult.Id);
            var seenNames = new HashSet<string>();
            foreach (var file in files.Where(f => f.Role == GgufFileRole.MainWeights))
            {
                var groupedName = GgufQuantizationParser.RemoveShardSuffix(file.FileName);
                if (seenNames.Add(groupedName))
                {
                    RepoFiles.Add(new HfFileRowViewModel(file));
                }
            }
        }
        catch (HttpRequestException ex)
        {
            StatusMessage = $"Erreur lors de la lecture du dépôt : {ex.Message}";
        }
        finally
        {
            IsLoadingFiles = false;
        }
    }

    [RelayCommand]
    private async Task DownloadSelectedFileAsync()
    {
        if (SelectedSearchResult is null || SelectedFile is null)
        {
            return;
        }

        IsDownloading = true;
        SelectedFile.DownloadProgressPercent = 0;
        try
        {
            var config = await _configRepository.LoadAsync();
            var destinationDir = Path.Combine(config.Storage.LlamaModelsPath, SelectedSearchResult.Id.Replace('/', '_'));
            var destinationPath = Path.Combine(destinationDir, SelectedFile.FileName);
            var url = $"https://huggingface.co/{SelectedSearchResult.Id}/resolve/main/{SelectedFile.FileName}";

            var progress = new Progress<DownloadProgress>(p => SelectedFile.DownloadProgressPercent = p.PercentComplete);
            await _downloader.DownloadAsync(url, destinationPath, progress);

            LocalFilePath = destinationPath;

            var repoFiles = await _huggingFaceClient.ListRepoFilesAsync(SelectedSearchResult.Id);
            _hasMultimodalProjector = repoFiles.Any(f => f.Role == GgufFileRole.MultimodalProjector);
            _hasDraftModel = repoFiles.Any(f => f.Role == GgufFileRole.DraftModel);

            if (string.IsNullOrWhiteSpace(TierLabel))
            {
                TierLabel = $"{SelectedSearchResult.RepoName} ({SelectedFile.QuantLabel})";
            }

            StatusMessage = $"Téléchargé : {destinationPath}";
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            StatusMessage = $"Échec du téléchargement : {ex.Message}";
        }
        finally
        {
            IsDownloading = false;
        }
    }

    [RelayCommand]
    private async Task AnalyzeAsync()
    {
        IsAnalyzing = true;
        AiJustification = null;
        AiAgreement = null;
        try
        {
            _metadata = null;
            if (Engine == EngineKind.LlamaCpp && !string.IsNullOrWhiteSpace(LocalFilePath) && File.Exists(LocalFilePath))
            {
                try
                {
                    _metadata = _ggufReader.Read(LocalFilePath);
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    StatusMessage = $"Lecture des métadonnées impossible : {ex.Message}";
                }
            }

            var facts = new CandidateModelFacts
            {
                DisplayName = TierLabel,
                FileSizeBytes = _metadata?.TotalFileSizeBytes ?? (LocalFilePath is not null && File.Exists(LocalFilePath) ? new FileInfo(LocalFilePath).Length : 0),
                QuantizationLabel = _metadata?.QuantizationLabel ?? SelectedFile?.QuantLabel,
                GgufMetadata = _metadata,
                HasMultimodalProjector = _hasMultimodalProjector,
                HasDraftModel = _hasDraftModel,
            };

            var hardware = _hardwareMonitor.Current;
            Recommendation = _advisor.Evaluate(hardware, facts, desiredContextSize: 4096);
            ApplyRecommendationToDraft();
        }
        finally
        {
            IsAnalyzing = false;
        }
    }

    private void ApplyRecommendationToDraft()
    {
        if (Recommendation is not { } rec)
        {
            return;
        }

        _draftTier.Settings.ContextMemory.ContextSize = rec.RecommendedContextSize;

        if (rec.MoeRecommendation is { } moe)
        {
            _draftTier.Settings.MoeOffload.CpuLayerIndices = moe.MoeLayersInOrder
                .Take(moe.RecommendedNCpuMoe)
                .Select(l => l.Index)
                .ToHashSet();
            _draftTier.Settings.ApplyRecommendedLoadModeIfUnset();
        }
        else if (rec.RecommendedGpuLayers is { } gpuLayers)
        {
            _draftTier.Settings.GpuOffload.GpuLayers = new GpuLayerSpec { Mode = GpuLayerMode.Explicit, ExplicitCount = gpuLayers };
        }

        if (_hasMultimodalProjector)
        {
            StatusMessage = "Ce dépôt propose aussi un projecteur multimodal — utilisez Recherche Hugging Face après création pour l'associer.";
        }
    }

    [RelayCommand]
    private async Task AskAiAsync()
    {
        if (Recommendation is null)
        {
            return;
        }

        IsAskingAi = true;
        try
        {
            var tags = await _ollamaApiClient.ListTagsAsync();
            var modelName = tags.OrderBy(t => t.SizeBytes).Select(t => t.Name).FirstOrDefault();
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
                $"RAM {FormatGiB(hardware.TotalRamBytes)} au total ({FormatGiB(ConfigurationAdvisor.SystemRamReserveBytes)} réservés au système, {FormatGiB(availableRamBytes)} disponibles pour l'IA en permanence).";
            var modelKind = Recommendation.MoeRecommendation is not null ? "MoE" : "dense";
            var modelSummary = $"{TierLabel} ({modelKind}, quantification {SelectedFile?.QuantLabel ?? "inconnue"}).";
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

            var raw = await _ollamaApiClient.ChatOnceAsync(request, AdvisorPromptBuilder.JsonSchema);
            try
            {
                var parsed = JsonSerializer.Deserialize<AiAdvisorStructuredResponse>(raw);
                AiAgreement = parsed?.Agreement;
                AiJustification = string.IsNullOrWhiteSpace(parsed?.Justification) ? raw : parsed.Justification;
            }
            catch (JsonException)
            {
                AiJustification = raw;
            }
        }
        catch (HttpRequestException ex)
        {
            StatusMessage = $"Erreur lors de l'appel à l'IA : {ex.Message}";
        }
        catch (Exception ex) when (ex is TaskCanceledException or TimeoutException)
        {
            StatusMessage = "Le modèle n'a pas répondu à temps (chargement trop long).";
        }
        finally
        {
            IsAskingAi = false;
        }
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        _draftTier.Label = TierLabel;
        _draftTier.Engine = Engine;
        _draftTier.SystemPrompt = SystemPrompt;

        if (Engine == EngineKind.Ollama)
        {
            _draftTier.OllamaBaseModel = OllamaBaseModel;
            _draftTier.OllamaCustomModelName = OllamaCustomModelName;
        }
        else
        {
            _draftTier.LlamaCppSource = new LlamaCppModelSource
            {
                LocalFilePath = LocalFilePath,
                HfRepoId = SelectedSearchResult?.Id,
                HfFile = SelectedFile?.FileName,
                QuantHint = SelectedFile?.QuantLabel,
            };
        }

        var config = await _configRepository.LoadAsync();
        ModelProfile profile;
        if (IsNewProfile)
        {
            profile = new ModelProfile { Name = NewProfileName, Description = NewProfileDescription };
            config.Profiles.Add(profile);
        }
        else if (SelectedExistingProfile is not null)
        {
            profile = config.Profiles.FirstOrDefault(p => p.Id == SelectedExistingProfile.Id) ?? SelectedExistingProfile;
        }
        else
        {
            return;
        }

        profile.Tiers.Add(_draftTier);
        await _configRepository.SaveAsync(config);
        CreatedSuccessfully = true;
    }

    private static string FormatGiB(long bytes) => LocalIA.App.Converters.BytesToGigabytesConverter.Format(bytes);
}
