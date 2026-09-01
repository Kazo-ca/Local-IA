namespace LocalIA.Core.Models;

/// <summary>Une entrée de l'historique des requêtes routées — construite une fois la requête
/// entièrement terminée (succès ou échec), jamais partiellement visible.</summary>
public sealed record RouterRequestHistoryEntry
{
    public required Guid Id { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public required TimeSpan Duration { get; init; }
    public required string Method { get; init; }
    public required string Path { get; init; }
    public string? ResolvedModelId { get; init; }
    public EngineKind? ResolvedEngine { get; init; }
    public string? ResolvedProfileName { get; init; }
    public string? ResolvedTierLabel { get; init; }
    public RouterLoadOutcome? LoadOutcome { get; init; }
    public required int StatusCode { get; init; }
    public long RequestBodyBytes { get; init; }
    public long ResponseBodyBytes { get; init; }
    public string? RequestBodyPreview { get; init; }
    public string? ErrorMessage { get; init; }
}
