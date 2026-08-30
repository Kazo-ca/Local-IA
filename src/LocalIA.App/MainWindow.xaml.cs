using System.ComponentModel;
using System.Windows;
using LocalIA.App.ViewModels;
using LocalIA.Core.Abstractions;

namespace LocalIA.App;

public partial class MainWindow : Window
{
    private readonly ProfilesViewModel _profilesViewModel;
    private readonly IAppConfigRepository _configRepository;
    private readonly IEngineOrchestrationService _orchestrationService;
    private bool _closeConfirmed;
    private bool _closePromptOpen;

    public MainWindow(
        ShellViewModel shellViewModel,
        ProfilesViewModel profilesViewModel,
        IAppConfigRepository configRepository,
        IEngineOrchestrationService orchestrationService)
    {
        InitializeComponent();
        DataContext = shellViewModel;
        _profilesViewModel = profilesViewModel;
        _configRepository = configRepository;
        _orchestrationService = orchestrationService;
        Closing += OnClosing;
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closeConfirmed)
        {
            return;
        }

        // Toujours annuler cette tentative pour pouvoir attendre proprement l'enregistrement
        // (Closing n'a pas de forme async ; bloquer le thread UI ici risquerait un blocage,
        // puisque SaveAsync capture le contexte de synchronisation WPF pour reprendre).
        e.Cancel = true;

        if (!_profilesViewModel.IsDirty)
        {
            await FinalizeCloseAsync();
            return;
        }

        if (_closePromptOpen)
        {
            // Un second clic sur Fermer (ou Alt+F4 répété) pendant que l'invite est déjà affichée :
            // ne pas empiler une seconde boîte de dialogue par-dessus la première.
            return;
        }

        _closePromptOpen = true;
        MessageBoxResult result;
        try
        {
            result = MessageBox.Show(
                "Des modifications n'ont pas été enregistrées. Voulez-vous les enregistrer avant de quitter ?",
                "LOCAL-IA — Modifications non enregistrées",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);
        }
        finally
        {
            _closePromptOpen = false;
        }

        if (result == MessageBoxResult.Cancel)
        {
            return;
        }

        if (result == MessageBoxResult.Yes)
        {
            await _profilesViewModel.SaveCommand.ExecuteAsync(null);
        }

        await FinalizeCloseAsync();
    }

    private async Task FinalizeCloseAsync()
    {
        var config = await _configRepository.LoadAsync();
        if (config.Preferences.StopEnginesOnExit)
        {
            await _orchestrationService.StopAllAsync();
        }

        _closeConfirmed = true;
        // Jamais Close() en synchrone ici : WPF considère la fenêtre "en cours de fermeture" pour
        // toute la durée de cette invocation de Closing, et un second Close() dans la même pile
        // d'appel lève une InvalidOperationException. Reporter via le dispatcher laisse cette
        // invocation se terminer proprement avant de refermer.
        _ = Dispatcher.BeginInvoke(Close);
    }
}
