using CommunityToolkit.Mvvm.Messaging;
using LocalIA.Core.Abstractions;
using LocalIA.Core.Advisor;
using LocalIA.Core.Gguf;
using LocalIA.Core.HuggingFace;
using LocalIA.Core.MoeOffload;
using LocalIA.Infrastructure.Clients;
using LocalIA.Infrastructure.Configuration;
using LocalIA.Infrastructure.Engines;
using LocalIA.Infrastructure.Hardware;
using LocalIA.Infrastructure.HuggingFace;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LocalIA.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    // Points par défaut du toolkit PowerShell existant (config/config.json).
    // Deviendront configurables via IAppConfigRepository en phase 4.
    private static readonly Uri DefaultOllamaBaseAddress = new("http://127.0.0.1:11434/");
    private static readonly Uri DefaultLlamaCppBaseAddress = new("http://127.0.0.1:8080/");

    public static IServiceCollection AddLocalIaInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);

        // Timeout généreux (pas 5s) : ces clients servent aussi bien des vérifications rapides
        // (health-check /api/tags) que des appels intrinsèquement lents (chargement à froid d'un
        // modèle avant la première réponse de /api/chat, streaming de /api/pull). Les méthodes de
        // vérification rapide gèrent déjà elles-mêmes TaskCanceledException/HttpRequestException et
        // retombent sur un résultat vide/false — un serveur local mort échoue par connexion refusée
        // (immédiat), jamais par une lente absence de réponse.
        services.AddHttpClient<IOllamaApiClient, OllamaApiClient>(client =>
        {
            client.BaseAddress = DefaultOllamaBaseAddress;
            client.Timeout = TimeSpan.FromMinutes(5);
        });

        // Client séparé pour /api/pull et /api/create : ce sont des téléchargements/constructions
        // de modèle qui peuvent dépasser plusieurs dizaines de minutes sur un gros modèle — le
        // timeout de 5 min ci-dessus s'applique à la requête entière (corps en streaming compris),
        // pas seulement aux en-têtes, et couperait ces opérations en plein milieu.
        services.AddHttpClient(OllamaApiClient.LongRunningHttpClientName, client =>
        {
            client.BaseAddress = DefaultOllamaBaseAddress;
            client.Timeout = Timeout.InfiniteTimeSpan;
        });

        services.AddHttpClient<ILlamaCppApiClient, LlamaCppApiClient>(client =>
        {
            client.BaseAddress = DefaultLlamaCppBaseAddress;
            client.Timeout = TimeSpan.FromMinutes(5);
        });

        services.AddSingleton<IOllamaProcessManager, OllamaProcessManager>();
        services.AddSingleton<ILlamaCppProcessManager, LlamaCppProcessManager>();
        services.AddSingleton<IEngineOrchestrationService, EngineOrchestrationService>();
        services.AddSingleton<IEngineInstaller, EngineInstaller>();
        services.AddSingleton<IAutostartService, AutostartService>();
        services.AddSingleton<LegacyConfigImporter>();
        services.AddSingleton<IAppConfigRepository, AppConfigRepository>();
        services.AddSingleton<IVsCodeConfigurationService, VsCodeConfigurationService>();
        services.AddSingleton<IGgufMetadataReader, GgufMetadataReader>();
        services.AddSingleton<IMoeVramCalculator, MoeVramCalculator>();
        services.AddSingleton<IConfigurationAdvisor, ConfigurationAdvisor>();

        services.AddHttpClient<IHuggingFaceClient, HuggingFaceClient>(client =>
        {
            client.BaseAddress = new Uri("https://huggingface.co/");
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("LocalIA-Desktop-Manager/1.0");
        });
        services.AddHttpClient("HuggingFaceDownload", client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("LocalIA-Desktop-Manager/1.0");
        });
        services.AddSingleton<IGgufDownloader, GgufDownloader>();

        services.AddSingleton<HardwareMonitoringService>();
        services.AddSingleton<IHardwareMonitorService>(sp => sp.GetRequiredService<HardwareMonitoringService>());
        services.AddHostedService(sp => sp.GetRequiredService<HardwareMonitoringService>());

        return services;
    }
}
