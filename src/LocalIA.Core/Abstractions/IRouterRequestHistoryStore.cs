using LocalIA.Core.Models;

namespace LocalIA.Core.Abstractions;

/// <summary>Historique des requêtes routées, en mémoire uniquement (perdu au redémarrage de
/// l'app) et borné à `RouterSettings.MaxHistoryEntries` — même idée que le journal en direct du
/// Dashboard, mais avec des champs structurés plutôt que des lignes de texte pré-formatées.</summary>
public interface IRouterRequestHistoryStore
{
    void Add(RouterRequestHistoryEntry entry);
    void Clear();
    IReadOnlyList<RouterRequestHistoryEntry> Snapshot();

    event EventHandler<RouterRequestHistoryEntry>? EntryAdded;
}
