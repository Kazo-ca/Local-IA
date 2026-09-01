using System.Windows;
using LocalIA.App.Views;
using LocalIA.Core.Abstractions;

namespace LocalIA.App.Router;

/// <summary>
/// Implémentation côté App de <see cref="IRouterConflictPrompter"/> — a besoin d'une fenêtre et
/// du thread UI, contrairement à l'interface (Core) et à l'arbitre qui l'appelle (Infrastructure).
/// Appelée depuis un thread de requête HTTP en arrière-plan du routeur : bascule sur le thread UI
/// via le Dispatcher pour afficher la fenêtre, puis résout la Task attendue quand l'utilisateur
/// choisit un bouton.
/// </summary>
public sealed class RouterConflictPrompter : IRouterConflictPrompter
{
    public Task<RouterConflictResolution> PromptAsync(RouterConflictContext context, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<RouterConflictResolution>();

        // Pas de timeout en v1 (utilisateur local unique, assis devant la machine) — si la requête
        // entrante est annulée pendant que la boîte de dialogue est ouverte, au moins résoudre la
        // Task attendue pour ne pas bloquer indéfiniment l'arbitre ; la fenêtre elle-même reste
        // ouverte tant que l'utilisateur n'a pas cliqué (limitation documentée).
        ct.Register(() => tcs.TrySetResult(RouterConflictResolution.CancelIncoming));

        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            var window = new RouterConflictWindow(context);
            if (Application.Current.MainWindow is { IsVisible: true } mainWindow)
            {
                window.Owner = mainWindow;
            }

            window.ShowDialog();
            tcs.TrySetResult(window.Resolution);
        });

        return tcs.Task;
    }
}
