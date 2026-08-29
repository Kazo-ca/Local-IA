using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using LocalIA.Core.Abstractions;
using LocalIA.Core.Messaging;
using LocalIA.Core.Models;

namespace LocalIA.App.ViewModels;

public sealed partial class DashboardViewModel : ObservableObject, IRecipient<HardwareSnapshotChangedMessage>
{
    private const int MaxLogLines = 500;

    private readonly IOllamaProcessManager _ollamaProcessManager;
    private readonly ILlamaCppProcessManager _llamaCppProcessManager;
    private readonly IEngineOrchestrationService _orchestrationService;
    private readonly IAutostartService _autostartService;

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
        IMessenger messenger)
    {
        _ollamaProcessManager = ollamaProcessManager;
        _llamaCppProcessManager = llamaCppProcessManager;
        _orchestrationService = orchestrationService;
        _autostartService = autostartService;

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
    private async Task StartOllamaAsync()
    {
        await RunBusyAsync(() => _ollamaProcessManager.EnsureRunningAsync(new OllamaStartOptions
        {
            // Valeurs par défaut du toolkit PowerShell existant (config/config.json) ; deviendront
            // configurables via IAppConfigRepository en phase 4.
            ModelsPath = @"E:\ollama_models",
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
