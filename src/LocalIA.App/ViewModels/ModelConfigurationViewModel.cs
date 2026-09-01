using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalIA.App.Configuration;
using LocalIA.App.Navigation;
using LocalIA.Core.Abstractions;
using LocalIA.Core.Configuration;
using LocalIA.Core.Models;
using Microsoft.Win32;

namespace LocalIA.App.ViewModels;

public sealed partial class ModelConfigurationViewModel : ObservableObject
{
    private readonly INavigationService _navigationService;
    private readonly MoeViewModel _moeViewModel;
    private readonly ILlamaCppProcessManager _llamaCppProcessManager;
    private readonly IAppConfigRepository _configRepository;
    private readonly ILlamaCppLaunchPlanner _llamaCppLaunchPlanner;
    private readonly IEngineInstaller _engineInstaller;

    [ObservableProperty]
    private ModelTier? tier;

    public ConfigurationAdvisorViewModel Advisor { get; }

    [ObservableProperty]
    private string? generatedPreview;

    [ObservableProperty]
    private bool isStartingLlamaCpp;

    [ObservableProperty]
    private string? llamaCppStatusMessage;

    /// <summary>Vrai dès qu'une tentative de démarrage a révélé que llama-server.exe n'est pas
    /// configuré ou introuvable — fait apparaître le bouton « Installer llama.cpp » plutôt que de
    /// laisser le message d'erreur sans suite possible.</summary>
    [ObservableProperty]
    private bool isLlamaCppExecutableMissing;

    [ObservableProperty]
    private bool isInstallingLlamaCpp;

    /// <summary>Propriété observable dédiée plutôt qu'un binding XAML direct sur Tier.Engine :
    /// ModelTier n'implémente pas INotifyPropertyChanged, donc un changement de moteur (ComboBox)
    /// ne se propagerait pas tout seul jusqu'à un binding de Visibility — même raison que
    /// RefreshForEngineChange existe déjà pour les champs de réglages.</summary>
    [ObservableProperty]
    private bool isLlamaCppEngine;

    /// <summary>Levé quand l'utilisateur édite réellement un champ (pas sur un simple rafraîchissement
    /// de l'aperçu) — ProfilesViewModel s'y abonne pour marquer IsDirty=true.</summary>
    public event Action? SettingsChanged;

    public ObservableCollection<SettingsFieldDescriptor> SamplingFields { get; } = [];
    public ObservableCollection<SettingsFieldDescriptor> ContextMemoryFields { get; } = [];
    public ObservableCollection<SettingsFieldDescriptor> GpuOffloadFields { get; } = [];
    public ObservableCollection<SettingsFieldDescriptor> ServerNetworkingFields { get; } = [];
    public ObservableCollection<SettingsFieldDescriptor> MultimodalFields { get; } = [];
    public ObservableCollection<SettingsFieldDescriptor> SpeculativeDecodingFields { get; } = [];
    public ObservableCollection<SettingsFieldDescriptor> ReasoningFields { get; } = [];
    public ObservableCollection<SettingsFieldDescriptor> AdapterFields { get; } = [];

    public IReadOnlyList<GpuLayerMode> GpuLayerModes { get; } = [GpuLayerMode.Auto, GpuLayerMode.All, GpuLayerMode.Explicit];

    public ModelConfigurationViewModel(
        INavigationService navigationService,
        MoeViewModel moeViewModel,
        ConfigurationAdvisorViewModel advisor,
        ILlamaCppProcessManager llamaCppProcessManager,
        IAppConfigRepository configRepository,
        ILlamaCppLaunchPlanner llamaCppLaunchPlanner,
        IEngineInstaller engineInstaller)
    {
        _navigationService = navigationService;
        _moeViewModel = moeViewModel;
        Advisor = advisor;
        _llamaCppProcessManager = llamaCppProcessManager;
        _configRepository = configRepository;
        _llamaCppLaunchPlanner = llamaCppLaunchPlanner;
        _engineInstaller = engineInstaller;

        // "Appliquer" dans le panneau Conseiller mute Tier.Settings directement (pas via un
        // SettingsFieldDescriptor existant) : Rebuild() régénère les champs affichés avec les
        // nouvelles valeurs, et SettingsChanged marque le profil comme non enregistré, exactement
        // comme une édition manuelle dans les onglets ci-dessus.
        Advisor.RecommendationApplied += () =>
        {
            Rebuild();
            SettingsChanged?.Invoke();
        };
    }

    public void LoadTier(ModelTier newTier)
    {
        Tier = newTier;
        Rebuild();
        Advisor.LoadTier(newTier);
    }

    [RelayCommand]
    private void OpenMoePage()
    {
        if (Tier is null)
        {
            return;
        }

        _moeViewModel.LoadForTier(Tier);
        _navigationService.NavigateTo(_moeViewModel);
    }

    [RelayCommand]
    private async Task StartLlamaCppAsync()
    {
        if (Tier is not { Engine: EngineKind.LlamaCpp } tier)
        {
            return;
        }

        IsStartingLlamaCpp = true;
        LlamaCppStatusMessage = null;
        IsLlamaCppExecutableMissing = false;
        try
        {
            var config = await _configRepository.LoadAsync();
            if (string.IsNullOrWhiteSpace(config.LlamaCppServer.ExecutablePath) || !File.Exists(config.LlamaCppServer.ExecutablePath))
            {
                LlamaCppStatusMessage = "llama-server.exe non configuré ou introuvable.";
                IsLlamaCppExecutableMissing = true;
                return;
            }

            var source = tier.LlamaCppSource;
            var hasValidSource = (!string.IsNullOrWhiteSpace(source?.LocalFilePath) && File.Exists(source.LocalFilePath))
                || !string.IsNullOrWhiteSpace(source?.HfRepoId);
            if (!hasValidSource)
            {
                // Le moteur est llama.cpp : il faut un fichier GGUF local, pas un nom de modèle
                // Ollama (les champs OllamaBaseModel/OllamaCustomModelName ne sont pas utilisés
                // par ce moteur). Plutôt que d'échouer avec un message d'erreur, on demande
                // directement le fichier — Recherche Hugging Face / MoE restent les façons
                // habituelles de l'attacher, mais ne doivent pas être un détour obligatoire.
                var dialog = new OpenFileDialog { Filter = "Modèles GGUF (*.gguf)|*.gguf|Tous les fichiers (*.*)|*.*" };
                if (dialog.ShowDialog() != true)
                {
                    LlamaCppStatusMessage = "Aucun fichier GGUF sélectionné — démarrage annulé.";
                    return;
                }

                tier.LlamaCppSource ??= new LlamaCppModelSource();
                tier.LlamaCppSource.LocalFilePath = dialog.FileName;
                SettingsChanged?.Invoke();
            }

            LlamaCppLaunchSettings settings;
            try
            {
                settings = await _llamaCppLaunchPlanner.BuildAsync(tier, config);
            }
            catch (InvalidOperationException ex)
            {
                LlamaCppStatusMessage = ex.Message;
                return;
            }

            var started = await _llamaCppProcessManager.StartAsync(settings);
            LlamaCppStatusMessage = started
                ? $"llama-server démarré ({settings.ModelPath})."
                : "Échec du démarrage — vérifiez qu'aucune instance n'est déjà active et consultez le journal du Tableau de bord.";
        }
        finally
        {
            IsStartingLlamaCpp = false;
        }
    }

    [RelayCommand]
    private async Task InstallLlamaCppAsync()
    {
        IsInstallingLlamaCpp = true;
        try
        {
            var progress = new Progress<string>(status => LlamaCppStatusMessage = status);
            string executablePath;
            try
            {
                executablePath = await _engineInstaller.InstallLlamaCppAsync(progress);
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
            {
                LlamaCppStatusMessage = $"Échec de l'installation : {ex.Message}";
                return;
            }

            var config = await _configRepository.LoadAsync();
            config.LlamaCppServer.ExecutablePath = executablePath;
            await _configRepository.SaveAsync(config);

            IsLlamaCppExecutableMissing = false;
            LlamaCppStatusMessage = $"llama.cpp installé : {executablePath}";
        }
        finally
        {
            IsInstallingLlamaCpp = false;
        }
    }

    [RelayCommand]
    private void RefreshForEngineChange()
    {
        if (Tier is null)
        {
            return;
        }

        foreach (var field in AllFields())
        {
            field.CurrentEngine = Tier.Engine;
        }

        IsLlamaCppEngine = Tier.Engine == EngineKind.LlamaCpp;
        OnFieldValueChanged();
    }

    private void Rebuild()
    {
        SamplingFields.Clear();
        ContextMemoryFields.Clear();
        GpuOffloadFields.Clear();
        ServerNetworkingFields.Clear();
        MultimodalFields.Clear();
        SpeculativeDecodingFields.Clear();
        ReasoningFields.Clear();
        AdapterFields.Clear();

        if (Tier is null)
        {
            IsLlamaCppEngine = false;
            GeneratedPreview = null;
            return;
        }

        var engine = Tier.Engine;
        IsLlamaCppEngine = engine == EngineKind.LlamaCpp;
        Fill(SamplingFields, Tier.Settings.Sampling, engine);
        Fill(ContextMemoryFields, Tier.Settings.ContextMemory, engine);
        Fill(GpuOffloadFields, Tier.Settings.GpuOffload, engine);
        Fill(ServerNetworkingFields, Tier.Settings.ServerNetworking, engine);
        Fill(MultimodalFields, Tier.Settings.Multimodal, engine);
        Fill(SpeculativeDecodingFields, Tier.Settings.SpeculativeDecoding, engine);
        Fill(ReasoningFields, Tier.Settings.Reasoning, engine);
        Fill(AdapterFields, Tier.Settings.Adapters, engine);

        UpdatePreview();
    }

    private void Fill(ObservableCollection<SettingsFieldDescriptor> target, object group, EngineKind engine)
    {
        foreach (var descriptor in SettingsFieldDescriptor.FromGroup(group, engine, OnFieldValueChanged))
        {
            target.Add(descriptor);
        }
    }

    private void OnFieldValueChanged()
    {
        UpdatePreview();
        SettingsChanged?.Invoke();
    }

    private IEnumerable<SettingsFieldDescriptor> AllFields() =>
        SamplingFields.Concat(ContextMemoryFields).Concat(GpuOffloadFields).Concat(ServerNetworkingFields)
            .Concat(MultimodalFields).Concat(SpeculativeDecodingFields).Concat(ReasoningFields).Concat(AdapterFields);

    [RelayCommand]
    private void RefreshPreview() => UpdatePreview();

    private void UpdatePreview()
    {
        if (Tier is null)
        {
            GeneratedPreview = null;
            return;
        }

        GeneratedPreview = Tier.Engine == EngineKind.Ollama
            ? "Paramètres Ollama (aperçu du corps envoyé à /api/create) :\n" +
              string.Join('\n', OllamaParameterBuilder.Build(Tier.Settings).Select(kv => $"{kv.Key} = {FormatPreviewValue(kv.Value)}"))
            : "Arguments CLI llama-server.exe (aperçu) :\n" +
              string.Join(' ', LlamaCppArgumentBuilder.Build(Tier.Settings).Select(QuoteIfNeeded));
    }

    private static string FormatPreviewValue(object value) => value switch
    {
        IEnumerable<string> list => string.Join(", ", list),
        IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    private static string QuoteIfNeeded(string arg) => arg.Contains(' ') ? $"\"{arg}\"" : arg;
}
