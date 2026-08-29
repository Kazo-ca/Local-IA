namespace LocalIA.Core.Abstractions;

public sealed record LlamaCppModelInfo(string Id, long? ParameterCount, string? FileType, int? ContextLength, long? SizeBytes);

public interface ILlamaCppApiClient : IChatEngineClient
{
    Task<bool> IsReachableAsync(CancellationToken ct = default);
    Task<IReadOnlyList<LlamaCppModelInfo>> ListModelsAsync(CancellationToken ct = default);
}
