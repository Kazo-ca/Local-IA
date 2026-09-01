using System.Net;
using System.Net.Sockets;
using LocalIA.Core.Abstractions;
using LocalIA.Core.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LocalIA.Infrastructure.Router;

/// <summary>
/// Héberge le serveur Kestrel du routeur dans ce même process WPF. Piloté comme un bouton
/// Démarrer/Arrêter (jamais <c>app.Run()</c>, qui bloquerait) — même idée que
/// OllamaProcessManager/LlamaCppProcessManager pour les moteurs d'inférence.
///
/// Le conteneur DI interne du <see cref="WebApplication"/> ne duplique pas le graphe DI externe :
/// les singletons déjà résolus (résolveur de modèles, arbitre de ressources, suivi des
/// connexions...) sont réinjectés tels quels depuis <paramref name="outerServices"/>. Seul le
/// client HTTP de transmission ("RouterForward") est propre au conteneur interne, puisqu'il n'a
/// pas d'équivalent côté externe.
/// </summary>
public sealed class RouterService(IServiceProvider outerServices, ILogger<RouterService> logger) : IRouterService, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private WebApplication? _app;

    public RouterStatus Status { get; private set; } = RouterStatus.Stopped;
    public int? ActivePort { get; private set; }

    public event EventHandler<RouterStatusChangedEventArgs>? StatusChanged;

    public async Task<bool> StartAsync(RouterSettings settings, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (Status is RouterStatus.Running)
            {
                return true;
            }

            SetStatus(RouterStatus.Starting, null);

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                // Ne jamais laisser Kestrel voir les vrais arguments de lancement du process WPF.
                Args = [],
                // Doit correspondre à un assembly réellement chargé : ASP.NET Core tente de
                // charger un "hosting startup assembly" du même nom au démarrage, et journalise
                // une exception critique (sans bloquer le démarrage, mais bruyant) si absent.
                ApplicationName = typeof(RouterService).Assembly.GetName().Name,
                ContentRootPath = AppContext.BaseDirectory,
            });

            IPAddress host;
            try
            {
                host = IPAddress.Parse(settings.Host);
            }
            catch (FormatException)
            {
                logger.LogWarning("Hôte du routeur invalide : {Host} — utilisation de 127.0.0.1.", settings.Host);
                host = IPAddress.Loopback;
            }

            builder.WebHost.ConfigureKestrel(options => options.Listen(host, settings.Port));

            builder.Services.AddSingleton(outerServices.GetRequiredService<IRouterModelResolver>());
            builder.Services.AddSingleton(outerServices.GetRequiredService<IRouterResourceArbiter>());
            builder.Services.AddSingleton(outerServices.GetRequiredService<IRouterConflictPrompter>());
            builder.Services.AddSingleton(outerServices.GetRequiredService<IRouterConnectionTracker>());
            builder.Services.AddSingleton(outerServices.GetRequiredService<IRouterRequestHistoryStore>());
            builder.Services.AddHttpClient(RouterRequestPipeline.RouterForwardClientName, c => c.Timeout = Timeout.InfiniteTimeSpan);
            builder.Services.AddSingleton<RouterRequestPipeline>();

            var app = builder.Build();
            app.Run(context => context.RequestServices.GetRequiredService<RouterRequestPipeline>().HandleAsync(context));

            try
            {
                await app.StartAsync(ct);
            }
            catch (Exception ex)
            {
                // Toujours disposer le WebApplication déjà construit (et potentiellement déjà lié
                // au port) avant de sortir — sinon toute exception hors IOException/SocketException
                // (ex. OperationCanceledException si l'hôte se ferme pendant un démarrage lent) le
                // fuite, puisque _app n'est assigné qu'après un démarrage réussi.
                await app.DisposeAsync();

                if (ex is IOException or SocketException)
                {
                    logger.LogWarning(
                        ex, "Échec du démarrage du routeur sur {Host}:{Port} — le port est peut-être déjà utilisé.",
                        settings.Host, settings.Port);
                    SetStatus(RouterStatus.Error, null);
                    return false;
                }

                SetStatus(RouterStatus.Stopped, null);
                throw;
            }

            _app = app;
            SetStatus(RouterStatus.Running, settings.Port);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_app is null)
            {
                return;
            }

            SetStatus(RouterStatus.Stopping, ActivePort);
            try
            {
                await _app.StopAsync(ct);
            }
            catch (Exception ex)
            {
                // Ne pas laisser Status bloqué sur "Stopping" indéfiniment (ex. délai d'arrêt de
                // Kestrel écoulé pendant qu'une connexion en streaming se termine) — le process est
                // de toute façon disposé juste après, donc autant refléter l'état réel.
                logger.LogWarning(ex, "Échec de l'arrêt propre du routeur — le process est tout de même disposé.");
            }
            finally
            {
                await _app.DisposeAsync();
                _app = null;
            }

            SetStatus(RouterStatus.Stopped, null);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    private void SetStatus(RouterStatus status, int? port)
    {
        Status = status;
        ActivePort = port;
        StatusChanged?.Invoke(this, new RouterStatusChangedEventArgs(status, port));
    }
}
