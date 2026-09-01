using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using LocalIA.Core.Abstractions;
using LocalIA.Core.Configuration;
using LocalIA.Core.Models;

namespace LocalIA.Infrastructure.Clients;

public sealed class OllamaApiClient(HttpClient httpClient, IHttpClientFactory httpClientFactory) : IOllamaApiClient
{
    public const string LongRunningHttpClientName = "OllamaLongRunning";


    public async Task<bool> IsReachableAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await httpClient.GetAsync("api/tags", ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<OllamaTagInfo>> ListTagsAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await httpClient.GetFromJsonAsync<TagsResponse>("api/tags", ct);
            return response?.Models.Select(m => new OllamaTagInfo(m.Name, m.Size)).ToList() ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return [];
        }
    }

    public async Task<IReadOnlyList<OllamaRunningModelInfo>> ListRunningModelsAsync(CancellationToken ct = default, string? hostOverride = null)
    {
        try
        {
            var uri = BuildRequestUri("api/ps", hostOverride);
            var response = await httpClient.GetFromJsonAsync<PsResponse>(uri, ct);
            return response?.Models
                .Select(m => new OllamaRunningModelInfo(m.Name, m.Size, m.SizeVram, m.ExpiresAt))
                .ToList() ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return [];
        }
    }

    private static Uri BuildRequestUri(string relativePath, string? hostOverride) =>
        hostOverride is { Length: > 0 } ? new Uri($"http://{hostOverride}/{relativePath}") : new Uri(relativePath, UriKind.Relative);

    public async Task<bool> UnloadModelAsync(string modelName, CancellationToken ct = default, string? hostOverride = null)
    {
        // Passe par la CLI (`ollama stop`) plutôt que par /api/generate avec keep_alive=0 :
        // l'encodage exact attendu par l'API pour "décharger immédiatement" varie selon les
        // versions d'Ollama, alors que la commande CLI est stable et documentée.
        try
        {
            var startInfo = new ProcessStartInfo("ollama")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                ArgumentList = { "stop", modelName },
            };

            if (hostOverride is { Length: > 0 })
            {
                // Sans ceci, `ollama stop` cible toujours l'instance par défaut (127.0.0.1:11434)
                // même si l'instance réellement configurée tourne sur un autre hôte/port.
                startInfo.Environment["OLLAMA_HOST"] = hostOverride;
            }

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            await process.WaitForExitAsync(ct);
            return process.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    public async Task<string> ChatOnceAsync(ChatRequest request, string? jsonSchema = null, CancellationToken ct = default)
    {
        object? format = jsonSchema is null ? null : JsonSerializer.Deserialize<JsonElement>(jsonSchema);
        var body = new
        {
            model = request.Model,
            messages = request.Messages.Select(m => new { role = m.Role.ToWireString(), content = m.Content }),
            stream = false,
            format,
            options = request.Temperature is { } temperature ? new { temperature } : null,
        };

        using var response = await httpClient.PostAsJsonAsync("api/chat", body, ct);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<ChatChunkDto>(cancellationToken: ct);
        return payload?.Message?.Content ?? "";
    }

    public async Task<bool> CopyModelAsync(string sourceModel, string destinationModel, CancellationToken ct = default)
    {
        try
        {
            using var response = await httpClient.PostAsJsonAsync("api/copy", new { source = sourceModel, destination = destinationModel }, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    public IAsyncEnumerable<OllamaOperationProgress> PullModelStreamAsync(string modelName, CancellationToken ct = default)
        => StreamProgressAsync("api/pull", new { model = modelName, stream = true }, ct);

    public IAsyncEnumerable<OllamaOperationProgress> CreateModelStreamAsync(OllamaCreateRequest request, CancellationToken ct = default)
        => StreamProgressAsync(
            "api/create",
            new
            {
                model = request.Model,
                from = request.From,
                system = request.System,
                parameters = OllamaParameterBuilder.Build(request.Settings),
                stream = true,
            },
            ct);

    private async IAsyncEnumerable<OllamaOperationProgress> StreamProgressAsync(
        string path, object body, [EnumeratorCancellation] CancellationToken ct)
    {
        // Client dédié, sans timeout : /api/pull et /api/create peuvent durer bien plus longtemps
        // que le timeout de 5 minutes du client principal (téléchargement d'un modèle de plusieurs
        // Go), qui s'applique à toute la requête, corps en streaming compris.
        using var longRunningClient = httpClientFactory.CreateClient(LongRunningHttpClientName);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        using var response = await longRunningClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            ProgressDto? dto;
            try
            {
                dto = JsonSerializer.Deserialize<ProgressDto>(line);
            }
            catch (JsonException)
            {
                continue;
            }

            if (dto is null)
            {
                continue;
            }

            yield return new OllamaOperationProgress(dto.Status ?? "", dto.Total, dto.Completed, dto.Error);
        }
    }

    private sealed class ProgressDto
    {
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("total")]
        public long? Total { get; set; }

        [JsonPropertyName("completed")]
        public long? Completed { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }

    public async IAsyncEnumerable<ChatStreamToken> StreamChatAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var body = new
        {
            model = request.Model,
            messages = request.Messages.Select(m => new { role = m.Role.ToWireString(), content = m.Content }),
            stream = true,
            options = request.Temperature is { } temperature ? new { temperature } : null,
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "api/chat") { Content = JsonContent.Create(body) };
        using var response = await httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            ChatChunkDto? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize<ChatChunkDto>(line);
            }
            catch (JsonException)
            {
                continue;
            }

            if (chunk is null)
            {
                continue;
            }

            yield return new ChatStreamToken
            {
                DeltaContent = chunk.Message?.Content,
                IsDone = chunk.Done,
                PromptTokens = chunk.PromptEvalCount,
                CompletionTokens = chunk.EvalCount,
            };

            if (chunk.Done)
            {
                yield break;
            }
        }
    }

    private sealed class ChatChunkDto
    {
        [JsonPropertyName("message")]
        public ChatMessageDto? Message { get; set; }

        [JsonPropertyName("done")]
        public bool Done { get; set; }

        [JsonPropertyName("prompt_eval_count")]
        public int? PromptEvalCount { get; set; }

        [JsonPropertyName("eval_count")]
        public int? EvalCount { get; set; }
    }

    private sealed class ChatMessageDto
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }

    private sealed class TagsResponse
    {
        [JsonPropertyName("models")]
        public List<TagDto> Models { get; set; } = [];
    }

    private sealed class TagDto
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }

    private sealed class PsResponse
    {
        [JsonPropertyName("models")]
        public List<PsDto> Models { get; set; } = [];
    }

    private sealed class PsDto
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("size_vram")]
        public long SizeVram { get; set; }

        [JsonPropertyName("expires_at")]
        public DateTimeOffset? ExpiresAt { get; set; }
    }
}
