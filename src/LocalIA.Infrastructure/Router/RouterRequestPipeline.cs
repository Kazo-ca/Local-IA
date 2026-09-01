using System.Diagnostics;
using System.Text;
using System.Text.Json;
using LocalIA.Core.Abstractions;
using LocalIA.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace LocalIA.Infrastructure.Router;

/// <summary>
/// Cœur du proxy inverse du routeur : extrait l'id de modèle (corps JSON en priorité, sinon
/// préfixe d'URL <c>/router/{modelId}/...</c>), délègue à <see cref="IRouterResourceArbiter"/>
/// pour s'assurer que le bon modèle est chargé, puis transmet la requête HTTP telle quelle
/// (méthode, en-têtes, corps) au backend résolu et retransmet la réponse telle quelle (y compris
/// le streaming SSE/NDJSON) — jamais de traduction de protocole. Capture une entrée d'historique
/// une fois la requête entièrement terminée (succès ou échec), jamais partiellement visible.
/// </summary>
public sealed class RouterRequestPipeline(
    IRouterModelResolver resolver,
    IRouterResourceArbiter arbiter,
    IRouterConnectionTracker tracker,
    IRouterRequestHistoryStore historyStore,
    IHttpClientFactory httpClientFactory,
    ILogger<RouterRequestPipeline> logger)
{
    public const string RouterForwardClientName = "RouterForward";

    private const long MaxBodyBytes = 10 * 1024 * 1024;
    private const int PreviewMaxChars = 2000;

    private static readonly HashSet<string> SkippedRequestHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host", "Content-Type", "Content-Length", "Connection", "Transfer-Encoding", "Keep-Alive", "Upgrade", "Proxy-Connection", "Trailer", "TE",
    };

    private static readonly HashSet<string> SkippedResponseHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection", "Transfer-Encoding", "Keep-Alive", "Content-Length", "Upgrade", "Proxy-Connection", "Trailer",
    };

    public async Task HandleAsync(HttpContext context)
    {
        var ct = context.RequestAborted;
        var path = context.Request.Path.Value ?? "/";

        if (string.Equals(path, "/router/__health", StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            await context.Response.WriteAsync("OK", ct);
            return;
        }

        var startedAt = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();
        var body = Array.Empty<byte>();
        RouterModelResolution? resolution = null;
        RouterLoadResult? loadResult = null;
        long responseBytes = 0;
        string? errorMessage = null;

        try
        {
            var (urlModelId, remainderPath) = RouterPathParser.Parse(path);

            var readBody = await ReadBodyAsync(context, ct);
            if (readBody is null)
            {
                context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                errorMessage = "Corps de requête trop volumineux.";
                await WriteJsonErrorAsync(context, errorMessage, ct);
                return;
            }

            body = readBody;

            var modelId = TryExtractModelIdFromBody(body) ?? urlModelId;
            if (modelId is null)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                errorMessage = "Aucun id de modèle trouvé (ni dans le corps JSON, ni dans l'URL).";
                await WriteJsonErrorAsync(context, errorMessage, ct);
                return;
            }

            resolution = await resolver.ResolveAsync(modelId, ct);
            if (resolution is null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                errorMessage = $"Modèle inconnu : « {modelId} ».";
                await WriteJsonErrorAsync(context, errorMessage, ct);
                return;
            }

            // Avant toute action de chargement — ordre important pour que l'arbitre et le
            // balayage d'inactivité voient toujours une activité à jour pour ce palier.
            using var lease = tracker.BeginLease(resolution.Tier.Id);

            try
            {
                loadResult = await arbiter.EnsureLoadedAsync(resolution, ct);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException)
            {
                logger.LogError(ex, "Échec de l'arbitrage des ressources pour « {ModelId} ».", modelId);
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
                errorMessage = "Échec du chargement du modèle.";
                await WriteJsonErrorAsync(context, errorMessage, ct);
                return;
            }

            if (loadResult.Outcome is RouterLoadOutcome.ConflictCancelled)
            {
                context.Response.StatusCode = StatusCodes.Status409Conflict;
                errorMessage = loadResult.ErrorMessage ?? "Conflit de ressources.";
                await WriteJsonErrorAsync(context, errorMessage, ct);
                return;
            }

            if (loadResult.Outcome is RouterLoadOutcome.Failed)
            {
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
                errorMessage = loadResult.ErrorMessage ?? "Échec du chargement du modèle.";
                await WriteJsonErrorAsync(context, errorMessage, ct);
                return;
            }

            try
            {
                responseBytes = await ForwardAsync(context, resolution, remainderPath, body, ct);
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex, "Échec de la transmission au moteur pour « {ModelId} ».", modelId);
                errorMessage = "Le moteur cible n'a pas répondu.";
                if (!context.Response.HasStarted)
                {
                    context.Response.StatusCode = StatusCodes.Status502BadGateway;
                    await WriteJsonErrorAsync(context, errorMessage, ct);
                }
            }
        }
        finally
        {
            stopwatch.Stop();
            historyStore.Add(new RouterRequestHistoryEntry
            {
                Id = Guid.NewGuid(),
                StartedAt = startedAt,
                Duration = stopwatch.Elapsed,
                Method = context.Request.Method,
                Path = path,
                ResolvedModelId = resolution?.ModelId,
                ResolvedEngine = resolution?.Engine,
                ResolvedProfileName = resolution?.Profile.Name,
                ResolvedTierLabel = resolution?.Tier.Label,
                LoadOutcome = loadResult?.Outcome,
                StatusCode = context.Response.StatusCode,
                RequestBodyBytes = body.Length,
                ResponseBodyBytes = responseBytes,
                RequestBodyPreview = BuildPreview(body),
                ErrorMessage = errorMessage,
            });
        }
    }

    private async Task<long> ForwardAsync(HttpContext context, RouterModelResolution resolution, string remainderPath, byte[] body, CancellationToken ct)
    {
        var targetUri = new UriBuilder("http", resolution.TargetHost, resolution.TargetPort, remainderPath)
        {
            Query = context.Request.QueryString.HasValue ? context.Request.QueryString.Value!.TrimStart('?') : "",
        }.Uri;

        using var requestMessage = new HttpRequestMessage(new HttpMethod(context.Request.Method), targetUri);
        if (body.Length > 0)
        {
            requestMessage.Content = new ByteArrayContent(body);
            if (context.Request.ContentType is { } contentType)
            {
                requestMessage.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
            }
        }

        foreach (var header in context.Request.Headers)
        {
            if (SkippedRequestHeaders.Contains(header.Key))
            {
                continue;
            }

            requestMessage.Headers.TryAddWithoutValidation(header.Key, (IEnumerable<string?>)header.Value);
        }

        var client = httpClientFactory.CreateClient(RouterForwardClientName);
        using var response = await client.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, ct);

        context.Response.StatusCode = (int)response.StatusCode;
        foreach (var header in response.Headers)
        {
            if (!SkippedResponseHeaders.Contains(header.Key))
            {
                context.Response.Headers[header.Key] = header.Value.ToArray();
            }
        }

        foreach (var header in response.Content.Headers)
        {
            if (!SkippedResponseHeaders.Contains(header.Key))
            {
                context.Response.Headers[header.Key] = header.Value.ToArray();
            }
        }

        var countingStream = new CountingStream(context.Response.Body);
        await response.Content.CopyToAsync(countingStream, ct);
        return countingStream.BytesWritten;
    }

    private static async Task<byte[]?> ReadBodyAsync(HttpContext context, CancellationToken ct)
    {
        await using var buffered = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await context.Request.Body.ReadAsync(chunk, ct)) > 0)
        {
            buffered.Write(chunk, 0, read);
            if (buffered.Length > MaxBodyBytes)
            {
                return null;
            }
        }

        return buffered.ToArray();
    }

    private static string? TryExtractModelIdFromBody(byte[] body)
    {
        if (body.Length == 0)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("model", out var modelProperty)
                && modelProperty.ValueKind == JsonValueKind.String)
            {
                return modelProperty.GetString();
            }
        }
        catch (JsonException)
        {
            // Corps non-JSON (ou vide) — l'id doit alors venir du préfixe d'URL.
        }

        return null;
    }

    private static string? BuildPreview(byte[] body)
    {
        if (body.Length == 0)
        {
            return null;
        }

        var text = Encoding.UTF8.GetString(body, 0, Math.Min(body.Length, PreviewMaxChars));
        return body.Length > PreviewMaxChars ? text + "…" : text;
    }

    private static Task WriteJsonErrorAsync(HttpContext context, string message, CancellationToken ct)
    {
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync(JsonSerializer.Serialize(new { error = message }), ct);
    }

    /// <summary>Compte les octets écrits vers la réponse — sert uniquement à alimenter
    /// ResponseBodyBytes dans l'historique, sans modifier le flux transmis.</summary>
    private sealed class CountingStream(Stream inner) : Stream
    {
        public long BytesWritten { get; private set; }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();

        public override Task FlushAsync(CancellationToken ct) => inner.FlushAsync(ct);

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            inner.Write(buffer, offset, count);
            BytesWritten += count;
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            await inner.WriteAsync(buffer, offset, count, ct);
            BytesWritten += count;
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        {
            await inner.WriteAsync(buffer, ct);
            BytesWritten += buffer.Length;
        }
    }
}
