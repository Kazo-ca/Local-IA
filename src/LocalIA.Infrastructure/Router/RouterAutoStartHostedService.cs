using LocalIA.Core.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LocalIA.Infrastructure.Router;

/// <summary>
/// Démarre le routeur automatiquement au lancement de l'app si l'utilisateur l'a activé dans
/// Paramètres. Ne doit jamais lever d'exception qui bloquerait le démarrage de l'app — c'est une
/// fonctionnalité optionnelle, pas un prérequis (même philosophie que le try/catch autour du
/// démarrage de l'hôte dans App.xaml.cs).
/// </summary>
public sealed class RouterAutoStartHostedService(
    IRouterService routerService, IAppConfigRepository configRepository, ILogger<RouterAutoStartHostedService> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            var config = await configRepository.LoadAsync(ct);
            if (!config.Router.Enabled)
            {
                return;
            }

            await routerService.StartAsync(config.Router, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Échec du démarrage automatique du routeur.");
        }
    }

    public Task StopAsync(CancellationToken ct) => routerService.StopAsync(ct);
}
