using LocalIA.Core.Configuration;
using LocalIA.Core.Models;

namespace LocalIA.Core.Abstractions;

public sealed record OllamaTagInfo(string Name, long SizeBytes);

public sealed record OllamaRunningModelInfo(string Name, long SizeBytes, long SizeVramBytes, DateTimeOffset? ExpiresAt);

public sealed record OllamaOperationProgress(string Status, long? Total = null, long? Completed = null, string? Error = null)
{
    public bool IsDone => Error is not null || Status is "success";
}

public sealed record OllamaCreateRequest(string Model, string From, string? System, ModelTierSettings Settings);

public interface IOllamaApiClient : IChatEngineClient
{
    Task<bool> IsReachableAsync(CancellationToken ct = default);
    Task<IReadOnlyList<OllamaTagInfo>> ListTagsAsync(CancellationToken ct = default);

    /// <param name="hostOverride">
    /// "host:port" à utiliser à la place du <c>BaseAddress</c> par défaut de l'<see cref="HttpClient"/>
    /// injecté (<c>127.0.0.1:11434</c>) — nécessaire pour interroger une instance Ollama configurée
    /// sur une autre adresse (<c>AppConfig.OllamaServer.Host</c>), comme le fait le routeur.
    /// </param>
    Task<IReadOnlyList<OllamaRunningModelInfo>> ListRunningModelsAsync(CancellationToken ct = default, string? hostOverride = null);

    /// <param name="hostOverride">Voir <see cref="ListRunningModelsAsync"/> — ici transmis comme
    /// variable d'environnement <c>OLLAMA_HOST</c> au process <c>ollama stop</c> lancé.</param>
    Task<bool> UnloadModelAsync(string modelName, CancellationToken ct = default, string? hostOverride = null);
    Task<bool> CopyModelAsync(string sourceModel, string destinationModel, CancellationToken ct = default);
    IAsyncEnumerable<OllamaOperationProgress> PullModelStreamAsync(string modelName, CancellationToken ct = default);
    IAsyncEnumerable<OllamaOperationProgress> CreateModelStreamAsync(OllamaCreateRequest request, CancellationToken ct = default);

    /// <summary>
    /// Appel non-streamé (réponse complète en un bloc), utilisé pour de courtes requêtes comme le
    /// conseiller IA plutôt que le chat interactif. jsonSchema optionnel force une sortie JSON
    /// structurée (Ollama "format") ; peut être ignoré silencieusement par certains modèles, d'où
    /// l'obligation pour l'appelant de tolérer une réponse non conforme.
    /// </summary>
    Task<string> ChatOnceAsync(ChatRequest request, string? jsonSchema = null, CancellationToken ct = default);
}
