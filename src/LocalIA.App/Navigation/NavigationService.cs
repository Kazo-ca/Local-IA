namespace LocalIA.App.Navigation;

public sealed class NavigationService : INavigationService
{
    public object? CurrentViewModel { get; private set; }

    public event Action<object?>? CurrentViewModelChanged;

    public void NavigateTo(object viewModel)
    {
        CurrentViewModel = viewModel;
        CurrentViewModelChanged?.Invoke(viewModel);
    }
}
