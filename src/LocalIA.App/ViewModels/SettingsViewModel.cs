using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalIA.Core.Abstractions;
using Microsoft.Win32;

namespace LocalIA.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IAppConfigRepository _configRepository;
    private readonly IEngineInstaller _engineInstaller;
    private readonly IVsCodeConfigurationService _vsCodeConfigurationService;

    [ObservableProperty]
    private bool isLoaded;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string? statusMessage;

    // Storage
    [ObservableProperty]
    private string ollamaModelsPath = "";

    [ObservableProperty]
    private string llamaModelsPath = "";

    // Ollama server
    [ObservableProperty]
    private string ollamaHost = "";

    [ObservableProperty]
    private string ollamaKeepAlive = "";

    [ObservableProperty]
    private int ollamaNumParallel;

    // llama.cpp server
    [ObservableProperty]
    private string llamaCppExecutablePath = "";

    [ObservableProperty]
    private string llamaCppDefaultHost = "";

    [ObservableProperty]
    private int llamaCppDefaultPort;

    [ObservableProperty]
    private bool isInstallingLlamaCpp;

    // VS Code
    [ObservableProperty]
    private bool isSyncingVsCode;

    // Preferences
    [ObservableProperty]
    private string? huggingFaceApiToken;

    [ObservableProperty]
    private bool stopEnginesOnExit;

    public SettingsViewModel(
        IAppConfigRepository configRepository,
        IEngineInstaller engineInstaller,
        IVsCodeConfigurationService vsCodeConfigurationService)
    {
        _configRepository = configRepository;
        _engineInstaller = engineInstaller;
        _vsCodeConfigurationService = vsCodeConfigurationService;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        var config = await _configRepository.LoadAsync();
        OllamaModelsPath = config.Storage.OllamaModelsPath;
        LlamaModelsPath = config.Storage.LlamaModelsPath;
        OllamaHost = config.OllamaServer.Host;
        OllamaKeepAlive = config.OllamaServer.KeepAlive;
        OllamaNumParallel = config.OllamaServer.NumParallel;
        LlamaCppExecutablePath = config.LlamaCppServer.ExecutablePath;
        LlamaCppDefaultHost = config.LlamaCppServer.DefaultHost;
        LlamaCppDefaultPort = config.LlamaCppServer.DefaultPort;
        HuggingFaceApiToken = config.Preferences.HuggingFaceApiToken;
        StopEnginesOnExit = config.Preferences.StopEnginesOnExit;
        IsLoaded = true;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        IsBusy = true;
        try
        {
            // Relit puis réécrit uniquement les champs de cet écran : ne jamais toucher Profiles,
            // que cette fenêtre ne charge jamais — un Enregistrer ici ne doit pas pouvoir effacer un
            // profil ajouté entre-temps par l'écran Profils ou l'Assistant nouveau modèle.
            var config = await _configRepository.LoadAsync();
            config.Storage.OllamaModelsPath = OllamaModelsPath;
            config.Storage.LlamaModelsPath = LlamaModelsPath;
            config.OllamaServer.Host = OllamaHost;
            config.OllamaServer.KeepAlive = OllamaKeepAlive;
            config.OllamaServer.NumParallel = OllamaNumParallel;
            config.LlamaCppServer.ExecutablePath = LlamaCppExecutablePath;
            config.LlamaCppServer.DefaultHost = LlamaCppDefaultHost;
            config.LlamaCppServer.DefaultPort = LlamaCppDefaultPort;
            config.Preferences.HuggingFaceApiToken = string.IsNullOrWhiteSpace(HuggingFaceApiToken) ? null : HuggingFaceApiToken;
            config.Preferences.StopEnginesOnExit = StopEnginesOnExit;

            await _configRepository.SaveAsync(config);
            StatusMessage = $"Enregistré à {DateTime.Now:HH:mm:ss}.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void BrowseOllamaModelsPath()
    {
        var dialog = new OpenFolderDialog { InitialDirectory = OllamaModelsPath };
        if (dialog.ShowDialog() == true)
        {
            OllamaModelsPath = dialog.FolderName;
        }
    }

    [RelayCommand]
    private void BrowseLlamaModelsPath()
    {
        var dialog = new OpenFolderDialog { InitialDirectory = LlamaModelsPath };
        if (dialog.ShowDialog() == true)
        {
            LlamaModelsPath = dialog.FolderName;
        }
    }

    [RelayCommand]
    private void BrowseLlamaCppExecutable()
    {
        var dialog = new OpenFileDialog { Filter = "llama-server.exe|llama-server.exe|Exécutables (*.exe)|*.exe" };
        if (dialog.ShowDialog() == true)
        {
            LlamaCppExecutablePath = dialog.FileName;
        }
    }

    [RelayCommand]
    private async Task InstallLlamaCppAsync()
    {
        IsInstallingLlamaCpp = true;
        try
        {
            var progress = new Progress<string>(status => StatusMessage = status);
            try
            {
                LlamaCppExecutablePath = await _engineInstaller.InstallLlamaCppAsync(progress);
                StatusMessage = $"llama.cpp installé : {LlamaCppExecutablePath} — pense à Enregistrer.";
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
            {
                StatusMessage = $"Échec de l'installation : {ex.Message}";
            }
        }
        finally
        {
            IsInstallingLlamaCpp = false;
        }
    }

    [RelayCommand]
    private async Task SyncVsCodeAsync()
    {
        IsSyncingVsCode = true;
        try
        {
            var config = await _configRepository.LoadAsync();
            var report = await _vsCodeConfigurationService.SyncChatLanguageModelsAsync(config);
            StatusMessage = $"chatLanguageModels.json synchronisé : {report.OllamaModelsWritten} modèle(s) Ollama, {report.LlamaCppModelsWritten} modèle(s) llama.cpp.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = $"Échec de la synchronisation : {ex.Message}";
        }
        finally
        {
            IsSyncingVsCode = false;
        }
    }

    [RelayCommand]
    private void OpenChatLanguageModelsInVsCode() => _vsCodeConfigurationService.OpenChatLanguageModelsInVsCode();
}
