using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalIA.App.Navigation;

namespace LocalIA.App.ViewModels;

public sealed partial class ShellViewModel : ObservableObject
{
    [ObservableProperty]
    private object? currentPageViewModel;

    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; }

    public ShellViewModel(
        INavigationService navigationService,
        DashboardViewModel dashboardViewModel,
        ChatViewModel chatViewModel,
        ProfilesViewModel profilesViewModel,
        ModelConfigurationViewModel modelConfigurationViewModel,
        MoeViewModel moeViewModel,
        HuggingFaceSearchViewModel huggingFaceSearchViewModel,
        SettingsViewModel settingsViewModel)
    {
        navigationService.CurrentViewModelChanged += vm => CurrentPageViewModel = vm;

        NavigationItems =
        [
            new NavigationItemViewModel("Tableau de bord", dashboardViewModel, navigationService),
            new NavigationItemViewModel("Profils / Modèles", profilesViewModel, navigationService),
            new NavigationItemViewModel("Chat / Test", chatViewModel, navigationService),
            new NavigationItemViewModel("Configuration du modèle", modelConfigurationViewModel, navigationService),
            new NavigationItemViewModel("MoE", moeViewModel, navigationService),
            new NavigationItemViewModel("Recherche Hugging Face", huggingFaceSearchViewModel, navigationService),
            new NavigationItemViewModel("Paramètres", settingsViewModel, navigationService),
        ];

        navigationService.NavigateTo(dashboardViewModel);
    }
}
