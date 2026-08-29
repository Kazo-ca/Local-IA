using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalIA.App.Navigation;
using LocalIA.Core.Abstractions;
using LocalIA.Core.HuggingFace;
using LocalIA.Core.Models;

namespace LocalIA.App.ViewModels;

public sealed partial class HuggingFaceSearchViewModel : ObservableObject
{
    private const string HuggingFaceProfileName = "Hugging Face";

    // Id fixe (pas le nom, éditable par l'utilisateur) pour retrouver de façon fiable le profil
    // auto-généré : le renommer dans Profils ne doit pas en faire recréer un second identique au
    // prochain ajout.
    private static readonly Guid HuggingFaceProfileId = Guid.Parse("f6a1c8e2-5b3d-4e7a-9c1f-2d8b4a6e0f3c");

    private readonly IHuggingFaceClient _client;
    private readonly IGgufDownloader _downloader;
    private readonly IOllamaApiClient _ollamaApiClient;
    private readonly IAppConfigRepository _configRepository;
    private readonly INavigationService _navigationService;
    private readonly ModelConfigurationViewModel _modelConfigurationViewModel;
    private readonly ProfilesViewModel _profilesViewModel;
    private CancellationTokenSource? _searchDebounceCts;
    private ModelTier? _lastAddedTier;

    [ObservableProperty]
    private string searchText = "";

    [ObservableProperty]
    private bool isSearching;

    [ObservableProperty]
    private HfModelSummary? selectedModel;

    [ObservableProperty]
    private bool isLoadingFiles;

    [ObservableProperty]
    private string? statusMessage;

    [ObservableProperty]
    private string? lastAddedTierLabel;

    public ObservableCollection<HfModelSummary> SearchResults { get; } = [];

    public ObservableCollection<HfFileRowViewModel> RepoFiles { get; } = [];

    public ObservableCollection<HfFileRowViewModel> AuxiliaryFiles { get; } = [];

    public HuggingFaceSearchViewModel(
        IHuggingFaceClient client,
        IGgufDownloader downloader,
        IOllamaApiClient ollamaApiClient,
        IAppConfigRepository configRepository,
        INavigationService navigationService,
        ModelConfigurationViewModel modelConfigurationViewModel,
        ProfilesViewModel profilesViewModel)
    {
        _client = client;
        _downloader = downloader;
        _ollamaApiClient = ollamaApiClient;
        _configRepository = configRepository;
        _navigationService = navigationService;
        _modelConfigurationViewModel = modelConfigurationViewModel;
        _profilesViewModel = profilesViewModel;
    }

    partial void OnSearchTextChanged(string value) => DebounceSearch();

    private void DebounceSearch()
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
            await RunSearchAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Une saisie plus récente a déjà annulé cette recherche — comportement attendu.
        }
    }

    private async Task RunSearchAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            SearchResults.Clear();
            return;
        }

        IsSearching = true;
        try
        {
            var results = await _client.SearchModelsAsync(SearchText, limit: 20, ct);
            ct.ThrowIfCancellationRequested();
            SearchResults.Clear();
            foreach (var result in results)
            {
                SearchResults.Add(result);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        finally
        {
            IsSearching = false;
        }
    }

    partial void OnSelectedModelChanged(HfModelSummary? value) => _ = LoadFilesAsync();

    private async Task LoadFilesAsync()
    {
        RepoFiles.Clear();
        AuxiliaryFiles.Clear();
        StatusMessage = null;
        if (SelectedModel is null)
        {
            return;
        }

        IsLoadingFiles = true;
        try
        {
            var files = await _client.ListRepoFilesAsync(SelectedModel.Id);
            var seenNames = new HashSet<string>();
            foreach (var file in files)
            {
                var groupedName = GgufQuantizationParser.RemoveShardSuffix(file.FileName);
                if (!seenNames.Add(groupedName))
                {
                    continue; // fichier multi-shard déjà représenté par son premier morceau
                }

                var row = new HfFileRowViewModel(file);
                if (file.Role == GgufFileRole.MainWeights)
                {
                    RepoFiles.Add(row);
                }
                else
                {
                    AuxiliaryFiles.Add(row);
                }
            }

            if (RepoFiles.Count == 0 && AuxiliaryFiles.Count == 0)
            {
                StatusMessage = "Aucun fichier .gguf trouvé dans ce dépôt.";
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
    private async Task AddToOllamaAsync(HfFileRowViewModel row)
    {
        if (SelectedModel is null)
        {
            return;
        }

        row.IsBusy = true;
        try
        {
            var baseModel = $"hf.co/{SelectedModel.Id}:{row.QuantLabel}";
            StatusMessage = $"Téléchargement de {baseModel} via Ollama…";

            await foreach (var progress in _ollamaApiClient.PullModelStreamAsync(baseModel))
            {
                row.DownloadProgressPercent = progress.Total is { } total && progress.Completed is { } completed && total > 0
                    ? 100.0 * completed / total
                    : row.DownloadProgressPercent;
                StatusMessage = progress.Status;

                if (progress.Error is not null)
                {
                    StatusMessage = $"Erreur Ollama : {progress.Error}";
                    return;
                }
            }

            var tier = new ModelTier
            {
                Label = $"{SelectedModel.RepoName} ({row.QuantLabel})",
                Engine = EngineKind.Ollama,
                OllamaBaseModel = baseModel,
                OllamaCustomModelName = baseModel,
            };

            await AddTierAndNavigateAsync(tier);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            StatusMessage = $"Échec du téléchargement via Ollama : {ex.Message}";
        }
        finally
        {
            row.IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AddToLlamaCppAsync(HfFileRowViewModel row)
    {
        if (SelectedModel is null)
        {
            return;
        }

        var tier = new ModelTier
        {
            Label = $"{SelectedModel.RepoName} ({row.QuantLabel})",
            Engine = EngineKind.LlamaCpp,
            LlamaCppSource = new LlamaCppModelSource
            {
                HfRepoId = SelectedModel.Id,
                HfFile = row.FileName,
                QuantHint = row.QuantLabel,
            },
        };

        StatusMessage = "Ajouté — llama-server téléchargera/résoudra ce modèle automatiquement au premier lancement (ou utilisez « Télécharger maintenant » pour le récupérer dès maintenant).";
        await AddTierAndNavigateAsync(tier);
    }

    [RelayCommand]
    private async Task DownloadNowAsync(HfFileRowViewModel row)
    {
        if (SelectedModel is null)
        {
            return;
        }

        row.IsBusy = true;
        row.DownloadProgressPercent = 0;
        try
        {
            var config = await _configRepository.LoadAsync();
            var destinationDir = Path.Combine(config.Storage.LlamaModelsPath, ToSafeDirectoryName(SelectedModel.Id));
            var destinationPath = Path.Combine(destinationDir, row.FileName);
            var url = $"https://huggingface.co/{SelectedModel.Id}/resolve/main/{row.FileName}";

            var progressReporter = new Progress<DownloadProgress>(p => row.DownloadProgressPercent = p.PercentComplete);
            await _downloader.DownloadAsync(url, destinationPath, progressReporter);

            StatusMessage = $"Téléchargé : {destinationPath}";

            var tier = new ModelTier
            {
                Label = $"{SelectedModel.RepoName} ({row.QuantLabel})",
                Engine = EngineKind.LlamaCpp,
                LlamaCppSource = new LlamaCppModelSource
                {
                    HfRepoId = SelectedModel.Id,
                    HfFile = row.FileName,
                    QuantHint = row.QuantLabel,
                    LocalFilePath = destinationPath,
                },
            };

            await AddTierAndNavigateAsync(tier);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            StatusMessage = $"Échec du téléchargement : {ex.Message}";
        }
        finally
        {
            row.IsBusy = false;
        }
    }

    private async Task AddTierAndNavigateAsync(ModelTier tier)
    {
        var config = await _configRepository.LoadAsync();
        // Repli par nom pour migrer une seule fois un profil déjà créé avant ce correctif (id
        // aléatoire d'origine) : une fois retrouvé, son id est verrouillé sur HuggingFaceProfileId
        // pour que les prochains ajouts le retrouvent même si l'utilisateur le renomme ensuite.
        var profile = config.Profiles.FirstOrDefault(p => p.Id == HuggingFaceProfileId)
            ?? config.Profiles.FirstOrDefault(p => p.Name == HuggingFaceProfileName);
        if (profile is not null)
        {
            profile.Id = HuggingFaceProfileId;
        }
        else
        {
            profile = new ModelProfile { Id = HuggingFaceProfileId, Name = HuggingFaceProfileName, Description = "Modèles ajoutés depuis la recherche Hugging Face." };
            config.Profiles.Add(profile);
        }

        profile.Tiers.Add(tier);
        await _configRepository.SaveAsync(config);
        await _profilesViewModel.RefreshFromDiskIfClean();

        // Retrouve le palier par Id dans les instances rechargées par ProfilesViewModel plutôt que
        // d'utiliser la variable locale `tier` : sans ça, tout réglage fait dans l'écran de
        // configuration qui suit modifierait un objet détaché, silencieusement perdu au prochain
        // "Enregistrer" depuis Profils (qui sérialise ses propres instances, pas celle-ci).
        var reloadedTier = _profilesViewModel.Profiles
            .SelectMany(p => p.Tiers)
            .FirstOrDefault(t => t.Id == tier.Id) ?? tier;

        _lastAddedTier = reloadedTier;
        LastAddedTierLabel = reloadedTier.Label;

        _modelConfigurationViewModel.LoadTier(reloadedTier);
        _navigationService.NavigateTo(_modelConfigurationViewModel);
    }

    [RelayCommand]
    private async Task AttachAuxiliaryFileAsync(HfFileRowViewModel row)
    {
        if (SelectedModel is null || _lastAddedTier is null)
        {
            StatusMessage = "Ajoutez d'abord un modèle principal (bouton « + llama.cpp » ci-dessus) avant d'associer un fichier complémentaire.";
            return;
        }

        row.IsBusy = true;
        row.DownloadProgressPercent = 0;
        try
        {
            var config = await _configRepository.LoadAsync();
            var destinationDir = Path.Combine(config.Storage.LlamaModelsPath, ToSafeDirectoryName(SelectedModel.Id));
            var destinationPath = Path.Combine(destinationDir, row.FileName);
            var url = $"https://huggingface.co/{SelectedModel.Id}/resolve/main/{row.FileName}";

            var progressReporter = new Progress<DownloadProgress>(p => row.DownloadProgressPercent = p.PercentComplete);
            await _downloader.DownloadAsync(url, destinationPath, progressReporter);

            // Recharge et retrouve le palier par Id : _lastAddedTier peut être une instance
            // détachée si le disque a été relu ailleurs (ex. Profils) depuis son ajout.
            var tier = config.Profiles.SelectMany(p => p.Tiers).FirstOrDefault(t => t.Id == _lastAddedTier.Id);
            if (tier is null)
            {
                StatusMessage = "Le palier ciblé est introuvable (a-t-il été supprimé ?).";
                return;
            }

            if (row.File.Role == GgufFileRole.MultimodalProjector)
            {
                tier.Settings.Multimodal.MmprojPath = destinationPath;
                StatusMessage = $"Projecteur multimodal associé à « {tier.Label} » : {destinationPath}";
            }
            else if (row.File.Role == GgufFileRole.DraftModel)
            {
                tier.Settings.SpeculativeDecoding.DraftModelPath = destinationPath;
                tier.Settings.SpeculativeDecoding.Type = Core.Configuration.SpeculativeType.DraftMtp;
                StatusMessage = $"Modèle brouillon associé à « {tier.Label} » (décodage spéculatif MTP activé) : {destinationPath}";
            }

            await _configRepository.SaveAsync(config);
            await _profilesViewModel.RefreshFromDiskIfClean();
            _lastAddedTier = tier;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            StatusMessage = $"Échec du téléchargement : {ex.Message}";
        }
        finally
        {
            row.IsBusy = false;
        }
    }

    private static string ToSafeDirectoryName(string repoId) => repoId.Replace('/', '_');
}
