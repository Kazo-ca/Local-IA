using CommunityToolkit.Mvvm.Messaging;
using LocalIA.Core.Abstractions;
using LocalIA.Core.Messaging;
using LocalIA.Core.Models;
using Microsoft.Extensions.Logging;

namespace LocalIA.Infrastructure.Router;

/// <summary>
/// Met en cache l'index construit par <see cref="RouterModelIndexBuilder"/> et le reconstruit
/// à la demande — au premier appel, puis dès que la configuration change (<see
/// cref="AppConfigChangedMessage"/>) — pour qu'une édition de profil pendant que le routeur
/// tourne soit prise en compte sans redémarrage.
/// </summary>
public sealed class RouterModelResolver : IRouterModelResolver, IRecipient<AppConfigChangedMessage>
{
    private readonly IAppConfigRepository _configRepository;
    private readonly ILogger<RouterModelResolver> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyDictionary<string, RouterModelResolution>? _cachedIndex;

    public RouterModelResolver(IAppConfigRepository configRepository, IMessenger messenger, ILogger<RouterModelResolver> logger)
    {
        _configRepository = configRepository;
        _logger = logger;
        messenger.RegisterAll(this);
    }

    public void Receive(AppConfigChangedMessage message) => _cachedIndex = null;

    public async Task<RouterModelResolution?> ResolveAsync(string modelId, CancellationToken ct = default)
    {
        var index = await GetIndexAsync(ct);
        return index.TryGetValue(modelId, out var resolution) ? resolution : null;
    }

    public async Task<IReadOnlyDictionary<string, RouterModelResolution>> GetIndexAsync(CancellationToken ct = default)
    {
        if (_cachedIndex is { } cached)
        {
            return cached;
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (_cachedIndex is { } stillCached)
            {
                return stillCached;
            }

            var config = await _configRepository.LoadAsync(ct);
            var index = RouterModelIndexBuilder.Build(config, _logger);
            _cachedIndex = index;
            return index;
        }
        finally
        {
            _gate.Release();
        }
    }
}
