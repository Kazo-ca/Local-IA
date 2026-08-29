using CommunityToolkit.Mvvm.ComponentModel;

namespace LocalIA.App.ViewModels;

public sealed partial class MoeLayerRowViewModel(int index, long sizeBytes, bool isOnGpu) : ObservableObject
{
    public int Index { get; } = index;
    public long SizeBytes { get; } = sizeBytes;

    [ObservableProperty]
    private bool isOnGpu = isOnGpu;
}
