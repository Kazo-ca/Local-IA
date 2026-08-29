using CommunityToolkit.Mvvm.ComponentModel;

namespace LocalIA.Core.Configuration;

public sealed partial class ServerNetworkingSettings : ObservableObject
{
    [property: EngineFlag(LlamaCppFlag = "--host", DisplayName = "Adresse d'écoute")]
    [ObservableProperty]
    private string? host;

    [property: EngineFlag(LlamaCppFlag = "--port", DisplayName = "Port")]
    [ObservableProperty]
    private int? port;

    [property: EngineFlag(LlamaCppFlag = "--api-key", DisplayName = "Clé API")]
    [ObservableProperty]
    private string? apiKey;

    [property: EngineFlag(LlamaCppFlag = "--timeout", DisplayName = "Timeout serveur (s)")]
    [ObservableProperty]
    private int? timeoutSeconds;

    [property: EngineFlag(LlamaCppFlag = "--parallel", DisplayName = "Slots parallèles")]
    [ObservableProperty]
    private int? parallelSlots;

    [property: EngineFlag(LlamaCppFlag = "--cont-batching", DisplayName = "Batching continu")]
    [ObservableProperty]
    private bool? continuousBatching;

    [property: EngineFlag(LlamaCppFlag = "--cache-prompt", DisplayName = "Cache de prompt")]
    [ObservableProperty]
    private bool? cachePrompt;

    [property: EngineFlag(LlamaCppFlag = "--cache-reuse", DisplayName = "Réutilisation du cache (taille min.)")]
    [ObservableProperty]
    private int? cacheReuse;

    [property: EngineFlag(LlamaCppFlag = "--cors-origins", DisplayName = "Origines CORS autorisées")]
    [ObservableProperty]
    private string? corsOrigins;

    [property: EngineFlag(LlamaCppFlag = "--threads-http", DisplayName = "Threads HTTP")]
    [ObservableProperty]
    private int? threadsHttp;
}
