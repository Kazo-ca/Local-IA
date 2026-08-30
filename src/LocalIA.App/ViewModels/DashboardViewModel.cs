using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using LocalIA.Core.Abstractions;
using LocalIA.Core.Messaging;
using LocalIA.Core.Models;
using Microsoft.Win32;

namespace LocalIA.App.ViewModels;

public sealed partial class DashboardViewModel : ObservableObject, IRecipient<HardwareSnapshotChangedMessage>
{
    private const int MaxLogLines = 500;

    private readonly IOllamaProcessManager _ollamaProcessManager;
    private readonly ILlamaCppProcessManager _llamaCppProcessManager;
    private readonly IEngineOrchestrationService _orchestrationService;
    private readonly IAutostartService _autostartService;
    private readonly IEngineInstaller _engineInstaller;
    private readonly IAppConfigRepository _configRepository;

    [ObservableProperty]
    private string cpuName = "";

    [ObservableProperty]
    private double cpuLoadPercent;

    [ObservableProperty]
    private long totalRamBytes;

    [ObservableProperty]
    private long usedRamBytes;

    [ObservableProperty]
    private double ramLoadPercent;

    [ObservableProperty]
    private string gpuName = "";

    [ObservableProperty]
    private long totalVramBytes;

    [ObservableProperty]
    private long usedVramBytes;

    [ObservableProperty]
    private double vramLoadPercent;

    [ObservableProperty]
    private double gpuLoadPercent;

    [ObservableProperty]
    private double gpuTemperatureCelsius;

    [ObservableProperty]
    private EngineStatus ollamaStatus;

    public bool IsOllamaNotInstalled => OllamaStatus == EngineStatus.NotInstalled;

    partial void OnOllamaStatusChanged(EngineStatus value) => OnPropertyChanged(nameof(IsOllamaNotInstalled));

    [ObservableProperty]
    private bool isInstallingOllama;

    [ObservableProperty]
    private string? installStatusMessage;

    [ObservableProperty]
    private int? ollamaProcessId;

    [ObservableProperty]
    private EngineStatus llamaCppStatus;

    [ObservableProperty]
    private int? llamaCppProcessId;

    [ObservableProperty]
    private DateTimeOffset lastRefreshedAt;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool isAutostartEnabled;

    public ObservableCollection<LoadedModelInfo> LoadedModels { get; } = [];

    public ObservableCollection<DiskSpaceInfo> Disks { get; } = [];

    public ObservableCollection<string> LogLines { get; } = [];

    public DashboardViewModel(
        IHardwareMonitorService hardwareMonitorService,
        IOllamaProcessManager ollamaProcessManager,
        ILlamaCppProcessManager llamaCppProcessManager,
        IEngineOrchestrationService orchestrationService,
        IAutostartService autostartService,
        IEngineInstaller engineInstaller,
        IAppConfigRepository configRepository,
        IMessenger messenger)
    {
        _ollamaProcessManager = ollamaProcessManager;
        _llamaCppProcessManager = llamaCppProcessManager;
        _orchestrationService = orchestrationService;
        _autostartService = autostartService;
        _engineInstaller = engineInstaller;
        _configRepository = configRepository;

        _ollamaProcessManager.LogLineReceived += (_, e) => OnLogLineReceived("Ollama", e);
        _llamaCppProcessManager.LogLineReceived += (_, e) => OnLogLineReceived("llama.cpp", e);

        messenger.RegisterAll(this);
        ApplySnapshot(hardwareMonitorService.Current);

        _ = RefreshAutostartStateAsync();
    }

    public void Receive(HardwareSnapshotChangedMessage message)
    {
        var snapshot = message.Value;
        RunOnUiThread(() => ApplySnapshot(snapshot));
    }

    [RelayCommand]
    private async Task InstallOllamaAsync()
    {
        IsInstallingOllama = true;
        InstallStatusMessage = null;
        try
        {
            var progress = new Progress<string>(status => InstallStatusMessage = status);
            await _engineInstaller.LaunchOllamaInstallerAsync(progress);
            InstallStatusMessage = "Installeur Ollama lancé — suis les instructions à l'écran, puis clique à nouveau sur Démarrer Ollama une fois l'installation terminée.";
        }
        catch (HttpRequestException ex)
        {
            InstallStatusMessage = $"Échec du téléchargement de l'installeur : {ex.Message}";
        }
        finally
        {
            IsInstallingOllama = false;
        }
    }

    [RelayCommand]
    private async Task StartOllamaAsync()
    {
        var config = await _configRepository.LoadAsync();
        await RunBusyAsync(() => _ollamaProcessManager.EnsureRunningAsync(new OllamaStartOptions
        {
            ModelsPath = config.Storage.OllamaModelsPath,
            Host = config.OllamaServer.Host,
            KeepAlive = config.OllamaServer.KeepAlive,
            NumParallel = config.OllamaServer.NumParallel,
        }));
    }

    [RelayCommand]
    private async Task UnloadAllModelsAsync()
    {
        if (!Confirm("Décharger tous les modèles de la mémoire (VRAM et RAM) ?"))
        {
            return;
        }

        await RunBusyAsync(() => _orchestrationService.UnloadAllModelsAsync());
    }

    [RelayCommand]
    private async Task StopOllamaAsync()
    {
        if (!Confirm("Arrêter le service Ollama ?"))
        {
            return;
        }

        await RunBusyAsync(() => _orchestrationService.StopOllamaAsync());
    }

    [RelayCommand]
    private async Task StopLlamaCppAsync()
    {
        if (!Confirm("Arrêter le serveur llama.cpp ? Toute génération en cours sera interrompue."))
        {
            return;
        }

        await RunBusyAsync(() => _orchestrationService.StopLlamaCppAsync());
    }

    [RelayCommand]
    private async Task StopAllAsync()
    {
        if (!Confirm("Arrêter tous les moteurs (Ollama et llama.cpp) et libérer toute la VRAM/RAM ?"))
        {
            return;
        }

        await RunBusyAsync(() => _orchestrationService.StopAllAsync());
    }

    [RelayCommand]
    private async Task ToggleAutostartAsync()
    {
        if (IsAutostartEnabled)
        {
            await _autostartService.DisableAsync();
        }
        else
        {
            await _autostartService.EnableAsync();
        }

        await RefreshAutostartStateAsync();
    }

    private async Task RefreshAutostartStateAsync()
    {
        // Pas de [RelayCommand] ici (appelée aussi bien en tâche perdue depuis le constructeur
        // qu'attendue depuis ToggleAutostartAsync) : intercepter directement plutôt que laisser
        // une erreur de lecture du raccourci/registre Démarrage disparaître silencieusement.
        try
        {
            IsAutostartEnabled = await _autostartService.IsEnabledAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            IsAutostartEnabled = false;
        }
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        IsBusy = true;
        try
        {
            await action();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CopyLogs()
    {
        if (LogLines.Count == 0)
        {
            return;
        }

        Clipboard.SetText(string.Join(Environment.NewLine, LogLines));
    }

    [RelayCommand]
    private void SaveLogs()
    {
        if (LogLines.Count == 0)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "Fichiers texte (*.log)|*.log|Tous les fichiers (*.*)|*.*",
            FileName = $"local-ia-logs_{DateTime.Now:yyyyMMdd_HHmmss}.log",
        };

        if (dialog.ShowDialog() == true)
        {
            File.WriteAllLines(dialog.FileName, LogLines);
        }
    }

    private static bool Confirm(string message)
        => MessageBox.Show(message, "LOCAL-IA", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    private void OnLogLineReceived(string engineLabel, EngineLogLineEventArgs e)
    {
        var line = $"[{e.Timestamp:HH:mm:ss}] [{engineLabel}/{e.Source}] {e.Text}";
        RunOnUiThread(() =>
        {
            LogLines.Add(line);
            while (LogLines.Count > MaxLogLines)
            {
                LogLines.RemoveAt(0);
            }
        });
    }

    private static void RunOnUiThread(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }

    private void ApplySnapshot(HardwareSnapshot snapshot)
    {
        CpuName = snapshot.CpuName;
        CpuLoadPercent = snapshot.CpuLoadPercent;
        TotalRamBytes = snapshot.TotalRamBytes;
        UsedRamBytes = snapshot.UsedRamBytes;
        RamLoadPercent = snapshot.RamLoadPercent;
        GpuName = snapshot.GpuName;
        TotalVramBytes = snapshot.TotalVramBytes;
        UsedVramBytes = snapshot.UsedVramBytes;
        VramLoadPercent = snapshot.VramLoadPercent;
        GpuLoadPercent = snapshot.GpuLoadPercent;
        GpuTemperatureCelsius = snapshot.GpuTemperatureCelsius;
        OllamaStatus = snapshot.OllamaStatus;
        OllamaProcessId = snapshot.OllamaProcessId;
        LlamaCppStatus = snapshot.LlamaCppStatus;
        LlamaCppProcessId = snapshot.LlamaCppProcessId;
        LastRefreshedAt = snapshot.CapturedAt;

        LoadedModels.Clear();
        foreach (var model in snapshot.LoadedModels)
        {
            LoadedModels.Add(model);
        }

        Disks.Clear();
        foreach (var disk in snapshot.Disks)
        {
            Disks.Add(disk);
        }
    }
}
