using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalIA.App.Navigation;
using LocalIA.App.Views;
using LocalIA.Core.Abstractions;
using LocalIA.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace LocalIA.App.ViewModels;

public sealed partial class ProfilesViewModel : ObservableObject
{
    private readonly IAppConfigRepository _configRepository;
    private readonly IOllamaApiClient _ollamaApiClient;
    private readonly IVsCodeConfigurationService _vsCodeConfigurationService;
    private readonly INavigationService _navigationService;
    private readonly ModelConfigurationViewModel _modelConfigurationViewModel;
    private readonly IServiceProvider _serviceProvider;
    private AppConfig _config = new();

    [ObservableProperty]
    private ModelProfile? selectedProfile;

    [ObservableProperty]
    private ModelTier? selectedTier;

    /// <summary>Propriété observable dédiée plutôt qu'un binding XAML direct sur SelectedTier.Engine :
    /// ModelTier n'implémente pas INotifyPropertyChanged, donc changer le moteur (ComboBox) ne
    /// rafraîchirait pas tout seul les Visibility qui en dépendent — même raison que IsLlamaCppEngine
    /// existe déjà sur ModelConfigurationViewModel. Rafraîchie explicitement sur changement de
    /// palier ET sur changement de moteur (EngineCombo_SelectionChanged, code-behind).</summary>
    [ObservableProperty]
    private bool isLlamaCppEngineSelected;

    public string? LlamaCppSourceDescription => SelectedTier?.LlamaCppSource switch
    {
        { LocalFilePath: { } path } => path,
        { HfRepoId: { } repo } source => $"{repo}{(source.QuantHint is { } q ? $":{q}" : "")} (résolu par llama.cpp au premier lancement, pas encore de fichier local)",
        _ => null,
    };

    public bool HasLlamaCppSource => LlamaCppSourceDescription is not null;

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
        IVsCodeConfigurationService vsCodeConfigurationService,
        INavigationService navigationService,
        ModelConfigurationViewModel modelConfigurationViewModel,
        IServiceProvider serviceProvider)
    {
        _configRepository = configRepository;
        _ollamaApiClient = ollamaApiClient;
        _vsCodeConfigurationService = vsCodeConfigurationService;
        _navigationService = navigationService;
        _modelConfigurationViewModel = modelConfigurationViewModel;
        _serviceProvider = serviceProvider;
        // Éditer un des ~90 champs de "Configuration du modèle" mutait déjà Tier.Settings (la même
        // instance que celle listée ici), mais ne marquait jamais IsDirty — rien n'invitait
        // l'utilisateur à enregistrer avant de fermer l'app ou de changer d'écran.
        _modelConfigurationViewModel.SettingsChanged += () => IsDirty = true;
        _ = ReloadAsync();
    }

    [RelayCommand]
    private async Task OpenNewModelWizardAsync()
    {
        // Résolu via IServiceProvider (pas d'injection directe de NewModelWizardWindow) : c'est une
        // fenêtre transient créée à la demande, pas un écran de navigation singleton comme les
        // autres ViewModels de cette classe.
        var window = _serviceProvider.GetRequiredService<NewModelWizardWindow>();
        window.Owner = Application.Current.MainWindow;
        var created = window.ShowDialog();
        if (created == true)
        {
            await RefreshFromDiskIfClean();
        }
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

        await MergeNewFromDiskAsync();
    }

    /// <summary>
    /// Fusionne dans l'état en mémoire les profils/paliers apparus sur disque depuis le dernier
    /// chargement (par Id, jamais en écrasant un profil/palier déjà connu ici) — jamais destructif,
    /// contrairement à un rechargement complet. Utilisée aussi bien après une action externe
    /// (Assistant, Recherche Hugging Face) que juste avant Enregistrer : cette fenêtre peut être
    /// restée ouverte pendant qu'une autre fenêtre ou l'Assistant ajoutait quelque chose ailleurs,
    /// et "Enregistrer" ne doit jamais silencieusement effacer un ajout qu'elle ignore.
    /// </summary>
    /// <returns>Nombre de profils et de paliers ajoutés depuis le disque.</returns>
    private async Task<(int ProfilesAdded, int TiersAdded)> MergeNewFromDiskAsync()
    {
        var onDisk = await _configRepository.LoadAsync();
        var knownProfileIds = _config.Profiles.Select(p => p.Id).ToHashSet();
        var profilesAdded = 0;
        var tiersAdded = 0;

        foreach (var diskProfile in onDisk.Profiles)
        {
            if (!knownProfileIds.Contains(diskProfile.Id))
            {
                _config.Profiles.Add(diskProfile);
                Profiles.Add(diskProfile);
                profilesAdded++;
                continue;
            }

            var localProfile = _config.Profiles.First(p => p.Id == diskProfile.Id);
            var knownTierIds = localProfile.Tiers.Select(t => t.Id).ToHashSet();
            foreach (var newTier in diskProfile.Tiers.Where(t => !knownTierIds.Contains(t.Id)))
            {
                localProfile.Tiers.Add(newTier);
                tiersAdded++;
            }
        }

        return (profilesAdded, tiersAdded);
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

    partial void OnSelectedTierChanged(ModelTier? value) => RefreshForEngineChange();

    /// <summary>Appelée après un changement de palier ET depuis EngineCombo_SelectionChanged
    /// (code-behind) après un changement de moteur sur le même palier — les deux cas doivent
    /// rafraîchir les mêmes propriétés dérivées.</summary>
    public void RefreshForEngineChange()
    {
        IsLlamaCppEngineSelected = SelectedTier?.Engine == EngineKind.LlamaCpp;
        OnPropertyChanged(nameof(LlamaCppSourceDescription));
        OnPropertyChanged(nameof(HasLlamaCppSource));
    }

    [RelayCommand]
    private void BrowseLlamaCppSourceFile()
    {
        if (SelectedTier is null)
        {
            return;
        }

        var dialog = new OpenFileDialog { Filter = "Modèles GGUF (*.gguf)|*.gguf|Tous les fichiers (*.*)|*.*" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        SelectedTier.LlamaCppSource ??= new LlamaCppModelSource();
        SelectedTier.LlamaCppSource.LocalFilePath = dialog.FileName;
        OnPropertyChanged(nameof(LlamaCppSourceDescription));
        OnPropertyChanged(nameof(HasLlamaCppSource));
        IsDirty = true;
    }

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
        // Toujours fusionner ce qui est apparu sur disque avant d'écraser le fichier : cette
        // fenêtre a pu rester ouverte pendant que l'Assistant nouveau modèle (ou une autre copie de
        // l'app) ajoutait un profil/palier directement via IAppConfigRepository — sans ça,
        // "Enregistrer" écraserait silencieusement cet ajout avec l'état plus ancien connu ici.
        var (profilesAdded, tiersAdded) = await MergeNewFromDiskAsync();

        // _config est chargé une seule fois au démarrage (constructeur) et jamais rafraîchi pour
        // ses champs hors Profiles — Storage/OllamaServer/LlamaCppServer/Preferences y sont donc
        // possiblement périmés si l'écran Paramètres (ou une autre fenêtre) les a modifiés depuis.
        // Cet écran n'édite jamais ces champs : repartir d'une lecture fraîche pour tout sauf
        // Profiles évite qu'Enregistrer ici écrase silencieusement un réglage global changé ailleurs.
        var fresh = await _configRepository.LoadAsync();
        fresh.Profiles = Profiles.ToList();
        await _configRepository.SaveAsync(fresh);
        _config = fresh;

        IsDirty = false;
        StatusMessage = profilesAdded > 0 || tiersAdded > 0
            ? $"Enregistré à {DateTime.Now:HH:mm:ss} ({profilesAdded} profil(s) et {tiersAdded} palier(s) apparus entre-temps ont été conservés) — {AppDataPathHint}"
            : $"Enregistré à {DateTime.Now:HH:mm:ss} — {AppDataPathHint}";
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

    /// <summary>Vrai si le palier a déjà une source de modèle llama.cpp valide (fichier local
    /// existant, ou dépôt Hugging Face) ; sinon propose un sélecteur de fichier GGUF — un palier
    /// llama.cpp créé à la main depuis cet écran n'a jamais de source tant qu'on ne lui en attache
    /// pas une (normalement via Recherche Hugging Face ou la page MoE), donc ce cas est courant,
    /// pas une erreur à se contenter d'afficher.</summary>
    private bool EnsureLlamaCppSource(ModelTier tier)
    {
        var source = tier.LlamaCppSource;
        var hasValidSource = (!string.IsNullOrWhiteSpace(source?.LocalFilePath) && File.Exists(source.LocalFilePath))
            || !string.IsNullOrWhiteSpace(source?.HfRepoId);
        if (hasValidSource)
        {
            StatusMessage = $"Fichier déjà attaché : {source!.LocalFilePath ?? source.HfRepoId}";
            return true;
        }

        var dialog = new OpenFileDialog { Filter = "Modèles GGUF (*.gguf)|*.gguf|Tous les fichiers (*.*)|*.*" };
        if (dialog.ShowDialog() != true)
        {
            StatusMessage = "Aucun fichier GGUF sélectionné.";
            return false;
        }

        tier.LlamaCppSource ??= new LlamaCppModelSource();
        tier.LlamaCppSource.LocalFilePath = dialog.FileName;
        IsDirty = true;
        StatusMessage = $"Fichier GGUF attaché : {dialog.FileName} — pense à Enregistrer.";
        return true;
    }

    [RelayCommand]
    private async Task BuildCustomModelAsync()
    {
        if (SelectedTier is null)
        {
            return;
        }

        if (SelectedTier.Engine == EngineKind.LlamaCpp)
        {
            // Ollama et llama.cpp ont des unités différentes : pour Ollama, "Créer" construit un
            // modèle nommé à partir d'un modèle de base tiré (OllamaBaseModel) ; llama.cpp n'a pas
            // cette étape de construction, juste un fichier GGUF local. "Créer" devient donc ici
            // "s'assurer qu'un fichier est attaché" (demande le fichier si besoin), sans démarrer.
            EnsureLlamaCppSource(SelectedTier);
            return;
        }

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
        if (SelectedTier is null || SelectedProfile is null)
        {
            return;
        }

        if (SelectedTier.Engine == EngineKind.LlamaCpp)
        {
            // Pas d'équivalent "définir comme actif" séparé pour llama.cpp (un seul serveur tourne
            // à la fois, cf. Limitations) : "actif" veut dire "démarré". Réutilise exactement le
            // même chemin que le bouton dédié de Configuration du modèle (fallback fichier GGUF
            // inclus), pour ne jamais avoir deux implémentations de ce lancement qui divergent.
            if (EnsureLlamaCppSource(SelectedTier))
            {
                _modelConfigurationViewModel.LoadTier(SelectedTier);
                await _modelConfigurationViewModel.StartLlamaCppCommand.ExecuteAsync(null);
                StatusMessage = _modelConfigurationViewModel.LlamaCppStatusMessage;
                await _vsCodeConfigurationService.ActivateProfileAsync(_config, SelectedProfile, SelectedTier);
            }

            return;
        }

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

            if (success)
            {
                await _vsCodeConfigurationService.ActivateProfileAsync(_config, SelectedProfile, SelectedTier);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }
}
