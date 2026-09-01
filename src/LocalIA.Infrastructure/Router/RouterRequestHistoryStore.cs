using System.Collections.Concurrent;
using CommunityToolkit.Mvvm.Messaging;
using LocalIA.Core.Abstractions;
using LocalIA.Core.Messaging;
using LocalIA.Core.Models;

namespace LocalIA.Infrastructure.Router;

public sealed class RouterRequestHistoryStore : IRouterRequestHistoryStore, IRecipient<AppConfigChangedMessage>
{
    private readonly ConcurrentQueue<RouterRequestHistoryEntry> _entries = new();
    private int _count;
    private int _maxEntries = new RouterSettings().MaxHistoryEntries;

    public RouterRequestHistoryStore(IAppConfigRepository configRepository, IMessenger messenger)
    {
        messenger.RegisterAll(this);
        _ = InitializeMaxEntriesAsync(configRepository);
    }

    public event EventHandler<RouterRequestHistoryEntry>? EntryAdded;

    public void Receive(AppConfigChangedMessage message) => _maxEntries = message.Value.Router.MaxHistoryEntries;

    public void Add(RouterRequestHistoryEntry entry)
    {
        _entries.Enqueue(entry);
        var count = Interlocked.Increment(ref _count);
        var max = Math.Max(1, _maxEntries);
        while (count > max && _entries.TryDequeue(out _))
        {
            count = Interlocked.Decrement(ref _count);
        }

        EntryAdded?.Invoke(this, entry);
    }

    public void Clear()
    {
        while (_entries.TryDequeue(out _))
        {
        }

        Interlocked.Exchange(ref _count, 0);
    }

    public IReadOnlyList<RouterRequestHistoryEntry> Snapshot() => _entries.ToArray();

    private async Task InitializeMaxEntriesAsync(IAppConfigRepository configRepository)
    {
        try
        {
            var config = await configRepository.LoadAsync();
            _maxEntries = config.Router.MaxHistoryEntries;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Valeur par défaut déjà en place — pas bloquant pour une fonctionnalité optionnelle.
        }
    }
}
