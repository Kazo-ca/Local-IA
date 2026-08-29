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
    Task<IReadOnlyList<OllamaRunningModelInfo>> ListRunningModelsAsync(CancellationToken ct = default);
    Task<bool> UnloadModelAsync(string modelName, CancellationToken ct = default);
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
