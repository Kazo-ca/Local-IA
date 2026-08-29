using LocalIA.Core.Models;

namespace LocalIA.Core.Abstractions;

public interface IAppConfigRepository
{
    Task<AppConfig> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(AppConfig config, CancellationToken ct = default);

    /// <summary>
    /// Relit directement le toolkit PowerShell legacy (config/config.json), indépendamment de
    /// l'existence d'app-config.json — contrairement à LoadAsync, qui n'importe le legacy qu'au
    /// tout premier lancement. Renvoie une liste vide si le dépôt legacy est introuvable.
    /// </summary>
    Task<IReadOnlyList<ModelProfile>> ImportLegacyAsync(CancellationToken ct = default);
}
