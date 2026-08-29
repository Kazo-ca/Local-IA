namespace LocalIA.App.Navigation;

public interface INavigationService
{
    object? CurrentViewModel { get; }

    event Action<object?>? CurrentViewModelChanged;

    void NavigateTo(object viewModel);
}
