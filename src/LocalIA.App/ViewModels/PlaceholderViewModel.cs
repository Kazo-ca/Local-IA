namespace LocalIA.App.ViewModels;

public sealed class PlaceholderViewModel(string featureName)
{
    public string FeatureName { get; } = featureName;
}
