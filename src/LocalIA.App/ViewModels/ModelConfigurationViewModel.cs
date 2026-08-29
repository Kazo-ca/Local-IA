using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalIA.App.Configuration;
using LocalIA.App.Navigation;
using LocalIA.Core.Abstractions;
using LocalIA.Core.Configuration;
using LocalIA.Core.Gguf;
using LocalIA.Core.Models;

namespace LocalIA.App.ViewModels;

public sealed partial class ModelConfigurationViewModel : ObservableObject
{
    private readonly INavigationService _navigationService;
    private readonly MoeViewModel _moeViewModel;
    private readonly ILlamaCppProcessManager _llamaCppProcessManager;
    private readonly IAppConfigRepository _configRepository;
    private readonly IGgufMetadataReader _ggufReader;

    [ObservableProperty]
    private ModelTier? tier;

    public ConfigurationAdvisorViewModel Advisor { get; }

    [ObservableProperty]
    private string? generatedPreview;

    [ObservableProperty]
    private bool isStartingLlamaCpp;

    [ObservableProperty]
    private string? llamaCppStatusMessage;

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
        IGgufMetadataReader ggufReader)
    {
        _navigationService = navigationService;
        _moeViewModel = moeViewModel;
        Advisor = advisor;
        _llamaCppProcessManager = llamaCppProcessManager;
        _configRepository = configRepository;
        _ggufReader = ggufReader;
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
        try
        {
            var config = await _configRepository.LoadAsync();
            if (string.IsNullOrWhiteSpace(config.LlamaCppServer.ExecutablePath))
            {
                LlamaCppStatusMessage = "Chemin de llama-server.exe non configuré (voir Paramètres).";
                return;
            }

            var totalMoeLayers = 0;
            var localPath = tier.LlamaCppSource?.LocalFilePath;
            if (!string.IsNullOrWhiteSpace(localPath) && File.Exists(localPath))
            {
                try
                {
                    totalMoeLayers = _ggufReader.Read(localPath).MoeLayers.Count();
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    // Non bloquant : le lancement peut se faire sans le placement MoE précis
                    // (--n-cpu-moe/--override-tensor seront simplement absents des arguments).
                }
            }

            LlamaCppLaunchSettings settings;
            try
            {
                settings = LlamaCppLaunchSettingsFactory.FromTier(
                    tier, config.LlamaCppServer.ExecutablePath, config.Preferences.HuggingFaceApiToken, totalMoeLayers);
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
