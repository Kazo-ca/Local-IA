using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalIA.Core.Abstractions;
using LocalIA.Core.Configuration;
using LocalIA.Core.Gguf;
using LocalIA.Core.Models;
using LocalIA.Core.MoeOffload;
using Microsoft.Win32;

namespace LocalIA.App.ViewModels;

public sealed partial class MoeViewModel : ObservableObject
{
    private readonly IGgufMetadataReader _ggufReader;
    private readonly IMoeVramCalculator _calculator;
    private bool _suppressLayerRowEvents;

    private ModelTier? _tier;
    private HashSet<int> _cpuLayerIndices = [];
    private bool _suppressSliderSync;

    [ObservableProperty]
    private string? ggufFilePath;

    [ObservableProperty]
    private string? tierLabel;

    [ObservableProperty]
    private GgufModelMetadata? metadata;

    [ObservableProperty]
    private long availableVramMiB;

    [ObservableProperty]
    private long safetyMarginMiB = 1024;

    [ObservableProperty]
    private int contextSize = 4096;

    [ObservableProperty]
    private MoeVramRecommendation? recommendation;

    [ObservableProperty]
    private bool isAdvancedMode;

    [ObservableProperty]
    private int simpleSliderValue;

    [ObservableProperty]
    private string? statusMessage;

    [ObservableProperty]
    private string? generatedArgsPreview;

    public ObservableCollection<MoeLayerRowViewModel> LayerRows { get; } = [];

    public MoeViewModel(IGgufMetadataReader ggufReader, IMoeVramCalculator calculator, IHardwareMonitorService hardwareMonitor)
    {
        _ggufReader = ggufReader;
        _calculator = calculator;
        var snapshot = hardwareMonitor.Current;
        AvailableVramMiB = snapshot.TotalVramBytes > 0
            ? (snapshot.TotalVramBytes - snapshot.UsedVramBytes) / 1024 / 1024
            : 12288;
    }

    /// <summary>Appelé depuis Profils/Configuration du modèle pour lier la page à un palier précis.</summary>
    public void LoadForTier(ModelTier tier)
    {
        _tier = tier;
        TierLabel = tier.Label;
        _cpuLayerIndices = [.. tier.Settings.MoeOffload.CpuLayerIndices];

        // Toujours (ré)affecter, y compris à null : `OnGgufFilePathChanged` ne se déclenche que si
        // la valeur change réellement, donc appeler LoadMetadata explicitement ensuite garantit que
        // les métadonnées/couches affichées correspondent bien à CE palier, jamais au précédent.
        GgufFilePath = tier.LlamaCppSource?.LocalFilePath;
        LoadMetadataCommand.Execute(null);
    }

    [RelayCommand]
    private void BrowseFile()
    {
        var dialog = new OpenFileDialog { Filter = "Modèles GGUF (*.gguf)|*.gguf|Tous les fichiers (*.*)|*.*" };
        if (dialog.ShowDialog() == true)
        {
            GgufFilePath = dialog.FileName;

            // Garde le palier en phase avec le fichier réellement analysé sur cette page : sans
            // ça, les indices de couches CPU calculés ici (sur CE fichier) pourraient être
            // enregistrés sur un palier pointant vers un fichier différent, avec un nombre de
            // couches MoE potentiellement différent.
            if (_tier is not null)
            {
                _tier.LlamaCppSource ??= new LlamaCppModelSource();
                _tier.LlamaCppSource.LocalFilePath = dialog.FileName;
            }
        }
    }

    partial void OnGgufFilePathChanged(string? value) => LoadMetadataCommand.Execute(null);

    [RelayCommand]
    private void LoadMetadata()
    {
        StatusMessage = null;
        Metadata = null;
        LayerRows.Clear();

        if (string.IsNullOrWhiteSpace(GgufFilePath))
        {
            return;
        }

        if (!File.Exists(GgufFilePath))
        {
            StatusMessage = "Fichier introuvable — le modèle n'a peut-être pas encore été téléchargé.";
            return;
        }

        try
        {
            Metadata = _ggufReader.Read(GgufFilePath);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            StatusMessage = $"Impossible de lire les métadonnées : {ex.Message}";
            return;
        }

        if (!Metadata.IsMoe)
        {
            StatusMessage = $"« {Path.GetFileName(GgufFilePath)} » ne semble pas être un modèle MoE (aucun tenseur d'experts détecté) — cette page ne s'applique qu'aux modèles à mélange d'experts.";
            Metadata = null;
            return;
        }

        // Ne réinitialise la sélection que si le palier n'a encore aucune préférence enregistrée.
        RecalculateCommand.Execute(null);
        if (_cpuLayerIndices.Count == 0 && Recommendation is not null)
        {
            SimpleSliderValue = Recommendation.RecommendedNCpuMoe;
            ApplySliderToSelection();
        }
        else
        {
            SimpleSliderValue = _cpuLayerIndices.Count;
        }

        RebuildLayerRows();
        UpdatePreview();
    }

    [RelayCommand]
    private void Recalculate()
    {
        if (Metadata is null)
        {
            Recommendation = null;
            return;
        }

        Recommendation = _calculator.Recommend(new MoeVramCalculatorInput
        {
            AvailableVramBytes = AvailableVramMiB * 1024 * 1024,
            SafetyMarginBytes = SafetyMarginMiB * 1024 * 1024,
            Model = Metadata,
            ContextSize = ContextSize,
            CacheTypeK = _tier?.Settings.ContextMemory.CacheTypeK ?? CacheQuantType.F16,
            CacheTypeV = _tier?.Settings.ContextMemory.CacheTypeV ?? CacheQuantType.F16,
        });

        UpdatePreview();
    }

    [RelayCommand]
    private void ApplyRecommendation()
    {
        if (Recommendation is null || Metadata is null)
        {
            return;
        }

        SimpleSliderValue = Recommendation.RecommendedNCpuMoe;
        ApplySliderToSelection();
    }

    partial void OnSimpleSliderValueChanged(int value)
    {
        if (!_suppressSliderSync)
        {
            ApplySliderToSelection();
        }
    }

    private void ApplySliderToSelection()
    {
        if (Metadata is null)
        {
            return;
        }

        var moeLayerIndicesInOrder = Metadata.MoeLayers.Select(l => l.Index).OrderBy(i => i).ToList();
        var clamped = Math.Clamp(SimpleSliderValue, 0, moeLayerIndicesInOrder.Count);
        _cpuLayerIndices = moeLayerIndicesInOrder.Take(clamped).ToHashSet();
        CommitSelection();
        RebuildLayerRows();
    }

    private void RebuildLayerRows()
    {
        _suppressLayerRowEvents = true;
        LayerRows.Clear();
        if (Metadata is not null)
        {
            foreach (var layer in Metadata.MoeLayers.OrderBy(l => l.Index))
            {
                var row = new MoeLayerRowViewModel(layer.Index, layer.MoeExpertsSizeBytes, isOnGpu: !_cpuLayerIndices.Contains(layer.Index));
                row.PropertyChanged += OnLayerRowPropertyChanged;
                LayerRows.Add(row);
            }
        }

        _suppressLayerRowEvents = false;
    }

    private void OnLayerRowPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_suppressLayerRowEvents || e.PropertyName != nameof(MoeLayerRowViewModel.IsOnGpu))
        {
            return;
        }

        _cpuLayerIndices = LayerRows.Where(r => !r.IsOnGpu).Select(r => r.Index).ToHashSet();

        _suppressSliderSync = true;
        SimpleSliderValue = _cpuLayerIndices.Count;
        _suppressSliderSync = false;

        CommitSelection();
    }

    private void CommitSelection()
    {
        if (_tier is not null)
        {
            _tier.Settings.MoeOffload.CpuLayerIndices = [.. _cpuLayerIndices];
        }

        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (Metadata is null)
        {
            GeneratedArgsPreview = Recommendation?.Explanation;
            return;
        }

        var totalMoeLayers = Metadata.MoeLayers.Count();
        var argsSource = _tier?.Settings.MoeOffload;
        var args = argsSource is not null
            ? argsSource.BuildLlamaCppArgs(totalMoeLayers)
            : new Core.Configuration.MoeOffloadSettings { CpuLayerIndices = _cpuLayerIndices }.BuildLlamaCppArgs(totalMoeLayers);

        var argsText = string.Join(' ', args.Select(a => a.Contains(' ', StringComparison.Ordinal) ? $"\"{a}\"" : a));
        GeneratedArgsPreview = (Recommendation?.Explanation ?? "") +
            (args.Count > 0 ? $"\n\nArguments llama-server générés :\n{argsText}" : "\n\n(Aucun réglage MoE explicite : llama.cpp gérera le placement automatiquement.)");
    }
}
