using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalIA.App.Navigation;
using LocalIA.Core.Abstractions;
using LocalIA.Core.Models;

namespace LocalIA.App.ViewModels;

public sealed partial class ProfilesViewModel : ObservableObject
{
    private readonly IAppConfigRepository _configRepository;
    private readonly IOllamaApiClient _ollamaApiClient;
    private readonly INavigationService _navigationService;
    private readonly ModelConfigurationViewModel _modelConfigurationViewModel;
    private AppConfig _config = new();

    [ObservableProperty]
    private ModelProfile? selectedProfile;

    [ObservableProperty]
    private ModelTier? selectedTier;

    [ObservableProperty]
    private bool isDirty;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string? statusMessage;

    public ObservableCollection<ModelProfile> Profiles { get; } = [];

    public IReadOnlyList<EngineKind> EngineOptions { get; } = [EngineKind.Ollama, EngineKind.LlamaCpp];

    public ProfilesViewModel(
        IAppConfigRepository configRepository,
        IOllamaApiClient ollamaApiClient,
        INavigationService navigationService,
        ModelConfigurationViewModel modelConfigurationViewModel)
    {
        _configRepository = configRepository;
        _ollamaApiClient = ollamaApiClient;
        _navigationService = navigationService;
        _modelConfigurationViewModel = modelConfigurationViewModel;
        // Éditer un des ~90 champs de "Configuration du modèle" mutait déjà Tier.Settings (la même
        // instance que celle listée ici), mais ne marquait jamais IsDirty — rien n'invitait
        // l'utilisateur à enregistrer avant de fermer l'app ou de changer d'écran.
        _modelConfigurationViewModel.SettingsChanged += () => IsDirty = true;
        _ = ReloadAsync();
    }

    [RelayCommand]
    private void OpenModelConfiguration()
    {
        if (SelectedTier is null)
        {
            return;
        }

        _modelConfigurationViewModel.LoadTier(SelectedTier);
        _navigationService.NavigateTo(_modelConfigurationViewModel);
    }

    /// <summary>
    /// Recharge depuis le disque — utilisé après qu'un autre écran (ex. Recherche Hugging Face)
    /// a ajouté un profil directement via le dépôt. Si des modifications non enregistrées sont en
    /// cours, ne les écrase pas : fusionne uniquement les profils/paliers apparus sur disque
    /// depuis (par Id) dans l'état en mémoire, pour qu'un "Enregistrer" ultérieur ne les efface pas.
    /// </summary>
    public async Task RefreshFromDiskIfClean()
    {
        if (!IsDirty)
        {
            await ReloadAsync();
            return;
        }

        var onDisk = await _configRepository.LoadAsync();
        var knownProfileIds = _config.Profiles.Select(p => p.Id).ToHashSet();

        foreach (var diskProfile in onDisk.Profiles)
        {
            if (!knownProfileIds.Contains(diskProfile.Id))
            {
                _config.Profiles.Add(diskProfile);
                Profiles.Add(diskProfile);
                continue;
            }

            var localProfile = _config.Profiles.First(p => p.Id == diskProfile.Id);
            var knownTierIds = localProfile.Tiers.Select(t => t.Id).ToHashSet();
            foreach (var newTier in diskProfile.Tiers.Where(t => !knownTierIds.Contains(t.Id)))
            {
                localProfile.Tiers.Add(newTier);
            }
        }
    }

    private async Task ReloadAsync()
    {
        _config = await _configRepository.LoadAsync();
        Profiles.Clear();
        foreach (var profile in _config.Profiles)
        {
            Profiles.Add(profile);
        }

        SelectedProfile = Profiles.FirstOrDefault();
        IsDirty = false;
    }

    partial void OnSelectedProfileChanged(ModelProfile? value) => SelectedTier = value?.Tiers.FirstOrDefault();

    [RelayCommand]
    private void AddProfile()
    {
        var profile = new ModelProfile { Name = "Nouveau profil" };
        Profiles.Add(profile);
        SelectedProfile = profile;
        IsDirty = true;
    }

    [RelayCommand]
    private void RemoveProfile()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        if (MessageBox.Show($"Supprimer le profil « {SelectedProfile.Name} » ?", "LOCAL-IA", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        Profiles.Remove(SelectedProfile);
        SelectedProfile = Profiles.FirstOrDefault();
        IsDirty = true;
    }

    [RelayCommand]
    private void AddTier()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        var tier = new ModelTier { Label = "Nouveau palier" };
        SelectedProfile.Tiers.Add(tier);
        SelectedTier = tier;
        IsDirty = true;
    }

    [RelayCommand]
    private void RemoveTier()
    {
        if (SelectedProfile is null || SelectedTier is null)
        {
            return;
        }

        SelectedProfile.Tiers.Remove(SelectedTier);
        SelectedTier = null;
        IsDirty = true;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        _config.Profiles = Profiles.ToList();
        await _configRepository.SaveAsync(_config);
        IsDirty = false;
        StatusMessage = $"Enregistré à {DateTime.Now:HH:mm:ss} — {AppDataPathHint}";
    }

    private static string AppDataPathHint => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LocalIA", "app-config.json");

    [RelayCommand]
    private async Task ImportLegacyAsync()
    {
        IsBusy = true;
        try
        {
            var imported = await _configRepository.ImportLegacyAsync();
            var existingLabels = Profiles.Select(p => p.Name).ToHashSet();
            var added = 0;
            foreach (var profile in imported.Where(p => !existingLabels.Contains(p.Name)))
            {
                Profiles.Add(profile);
                added++;
            }

            StatusMessage = added > 0
                ? $"{added} profil(s) importé(s) depuis config/config.json — pensez à Enregistrer."
                : "Aucun nouveau profil à importer (déjà présents, ou config/config.json introuvable).";
            if (added > 0)
            {
                IsDirty = true;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task PullBaseModelAsync()
    {
        if (SelectedTier?.OllamaBaseModel is not { } baseModel)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await foreach (var progress in _ollamaApiClient.PullModelStreamAsync(baseModel))
            {
                StatusMessage = progress.Total is { } total && progress.Completed is { } completed && total > 0
                    ? $"{progress.Status} ({100.0 * completed / total:0}%)"
                    : progress.Status;
            }

            StatusMessage = $"Modèle de base « {baseModel} » téléchargé.";
        }
        catch (HttpRequestException ex)
        {
            StatusMessage = $"Échec du téléchargement : {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task BuildCustomModelAsync()
    {
        if (SelectedTier is not { OllamaBaseModel: { } from, OllamaCustomModelName: { } model })
        {
            StatusMessage = "Le modèle de base et le nom du modèle personnalisé sont requis.";
            return;
        }

        IsBusy = true;
        try
        {
            await foreach (var progress in _ollamaApiClient.CreateModelStreamAsync(
                new OllamaCreateRequest(model, from, SelectedTier.SystemPrompt, SelectedTier.Settings)))
            {
                StatusMessage = progress.Status;
                if (progress.Error is not null)
                {
                    StatusMessage = $"Erreur : {progress.Error}";
                }
            }

            StatusMessage = $"Modèle « {model} » créé/mis à jour.";
        }
        catch (HttpRequestException ex)
        {
            StatusMessage = $"Échec de la création : {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SetActiveAsync()
    {
        if (SelectedTier?.OllamaCustomModelName is not { } model)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var success = await _ollamaApiClient.CopyModelAsync(model, "local-ia-active");
            StatusMessage = success
                ? $"« local-ia-active » pointe maintenant vers {model}."
                : "Échec : le modèle personnalisé existe-t-il déjà (bouton Créer) ?";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
