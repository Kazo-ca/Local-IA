using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LocalIA.App.Navigation;

public sealed partial class NavigationItemViewModel(string title, object viewModel, INavigationService navigationService)
    : ObservableObject
{
    public string Title { get; } = title;

    public object ViewModel { get; } = viewModel;

    [RelayCommand]
    private void Navigate() => navigationService.NavigateTo(ViewModel);
}
