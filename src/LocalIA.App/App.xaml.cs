using System.Windows;
using LocalIA.App.DependencyInjection;
using LocalIA.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LocalIA.App;

public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var builder = Host.CreateApplicationBuilder();
        builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
        builder.Services.AddLocalIaInfrastructure();
        builder.Services.AddLocalIaApp();

        _host = builder.Build();

        try
        {
            await _host.StartAsync();

            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            // DispatcherUnhandledException n'intercepterait qu'une exception levée APRÈS ce point ;
            // une erreur ici (ex. capteur matériel qui ne s'initialise pas) interromprait cette
            // méthode avant mainWindow.Show(), laissant le processus tourner indéfiniment sans
            // aucune fenêtre visible puisqu'aucune fenêtre n'aurait jamais été ouverte pour
            // déclencher ShutdownMode.OnLastWindowClose. Fermer explicitement dans ce cas précis.
            _host.Services.GetService<ILogger<App>>()?.LogCritical(ex, "Échec du démarrage de l'application");
            MessageBox.Show(
                $"LOCAL-IA n'a pas pu démarrer :\n\n{ex.Message}",
                "LOCAL-IA — Échec du démarrage",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        // Filet de sécurité : une erreur imprévue dans une fonctionnalité secondaire (ex. un appel
        // réseau qui échoue) ne doit jamais fermer toute l'application ni perdre l'état en cours
        // (chat, configuration non enregistrée...).
        _host?.Services.GetService<ILogger<App>>()?.LogError(e.Exception, "Exception non gérée interceptée au niveau de l'application");
        MessageBox.Show(
            $"Une erreur inattendue s'est produite et a été ignorée :\n\n{e.Exception.Message}",
            "LOCAL-IA — Erreur inattendue",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        e.Handled = true;
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            try
            {
                await _host.StopAsync();
            }
            finally
            {
                _host.Dispose();
            }
        }

        base.OnExit(e);
    }
}
