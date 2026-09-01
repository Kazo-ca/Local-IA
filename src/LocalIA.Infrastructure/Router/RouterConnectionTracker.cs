using System.Collections.Concurrent;
using LocalIA.Core.Abstractions;

namespace LocalIA.Infrastructure.Router;

public sealed class RouterConnectionTracker : IRouterConnectionTracker
{
    private sealed class TierLeaseState
    {
        public int ActiveCount;
        public DateTimeOffset LastActivityAt = DateTimeOffset.Now;
        public bool UnloadInProgress;
    }

    private sealed class Lease(TierLeaseState state) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            lock (state)
            {
                state.ActiveCount--;
                state.LastActivityAt = DateTimeOffset.Now;
            }
        }
    }

    // Un verrou par palier (pas un verrou global) : des paliers différents ne se bloquent jamais
    // l'un l'autre, seules les opérations concurrentes sur le MÊME palier sont sérialisées.
    private readonly ConcurrentDictionary<Guid, TierLeaseState> _states = new();

    public IDisposable BeginLease(Guid tierId)
    {
        var state = _states.GetOrAdd(tierId, _ => new TierLeaseState());
        lock (state)
        {
            state.ActiveCount++;
            state.LastActivityAt = DateTimeOffset.Now;
        }

        return new Lease(state);
    }

    public int GetActiveCount(Guid tierId)
    {
        if (!_states.TryGetValue(tierId, out var state))
        {
            return 0;
        }

        lock (state)
        {
            return state.ActiveCount;
        }
    }

    public DateTimeOffset? GetLastActivityAt(Guid tierId)
    {
        if (!_states.TryGetValue(tierId, out var state))
        {
            return null;
        }

        lock (state)
        {
            return state.LastActivityAt;
        }
    }

    public IReadOnlyDictionary<Guid, int> Snapshot()
    {
        var result = new Dictionary<Guid, int>();
        foreach (var (tierId, state) in _states)
        {
            lock (state)
            {
                result[tierId] = state.ActiveCount;
            }
        }

        return result;
    }

    public bool TryBeginEviction(Guid tierId, TimeSpan minIdleDuration)
    {
        if (!_states.TryGetValue(tierId, out var state))
        {
            return false;
        }

        lock (state)
        {
            if (state.ActiveCount != 0 || state.UnloadInProgress)
            {
                return false;
            }

            if (DateTimeOffset.Now - state.LastActivityAt < minIdleDuration)
            {
                return false;
            }

            state.UnloadInProgress = true;
            return true;
        }
    }

    public void ForceBeginEviction(Guid tierId)
    {
        if (!_states.TryGetValue(tierId, out var state))
        {
            return;
        }

        lock (state)
        {
            state.UnloadInProgress = true;
        }
    }

    public void EndEviction(Guid tierId)
    {
        if (!_states.TryGetValue(tierId, out var state))
        {
            return;
        }

        lock (state)
        {
            state.UnloadInProgress = false;
        }
    }
}
