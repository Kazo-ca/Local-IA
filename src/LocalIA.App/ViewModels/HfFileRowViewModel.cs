using CommunityToolkit.Mvvm.ComponentModel;
using LocalIA.Core.HuggingFace;

namespace LocalIA.App.ViewModels;

public sealed partial class HfFileRowViewModel(HfRepoFile file) : ObservableObject
{
    public HfRepoFile File { get; } = file;

    public string FileName => File.FileName;
    public string QuantLabel => File.QuantizationLabel ?? "?";
    public long? SizeBytes => File.SizeBytes;
    public string RoleDisplayName => GgufFileRoleClassifier.DisplayName(File.Role);
    public string RoleDescription => GgufFileRoleClassifier.Description(File.Role);

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private double? downloadProgressPercent;
}
