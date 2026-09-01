namespace LocalIA.Core.Models;

public enum RouterLoadOutcome
{
    AlreadyLoaded,
    Loaded,
    EvictedIdleThenLoaded,
    ConflictCancelled,
    ConflictDropped,
    Failed,
}

public sealed record RouterLoadResult(RouterLoadOutcome Outcome, string? ErrorMessage = null);
