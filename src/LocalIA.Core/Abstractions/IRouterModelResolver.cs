using LocalIA.Core.Models;

namespace LocalIA.Core.Abstractions;

public interface IRouterModelResolver
{
    Task<RouterModelResolution?> ResolveAsync(string modelId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, RouterModelResolution>> GetIndexAsync(CancellationToken ct = default);
}
