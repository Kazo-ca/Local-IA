using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using LocalIA.Core.Abstractions;
using LocalIA.Core.Models;

namespace LocalIA.Infrastructure.Clients;

public sealed class LlamaCppApiClient(HttpClient httpClient) : ILlamaCppApiClient
{
    public async Task<bool> IsReachableAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await httpClient.GetAsync("v1/models", ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<LlamaCppModelInfo>> ListModelsAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await httpClient.GetFromJsonAsync<ModelsResponse>("v1/models", ct);
            return response?.Data
                .Select(m => new LlamaCppModelInfo(m.Id, m.Meta?.NParams, m.Meta?.FType, m.Meta?.NCtx, m.Meta?.Size))
                .ToList() ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return [];
        }
    }

    public async IAsyncEnumerable<ChatStreamToken> StreamChatAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var body = new
        {
            model = request.Model,
            messages = request.Messages.Select(m => new { role = m.Role.ToWireString(), content = m.Content }),
            stream = true,
            temperature = request.Temperature,
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions") { Content = JsonContent.Create(body) };
        using var response = await httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var payload = line["data:".Length..].Trim();
            if (payload is "[DONE]" or "")
            {
                yield break;
            }

            ChatCompletionChunkDto? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize<ChatCompletionChunkDto>(payload);
            }
            catch (JsonException)
            {
                continue;
            }

            var choice = chunk?.Choices?.FirstOrDefault();
            if (choice is null)
            {
                continue;
            }

            yield return new ChatStreamToken
            {
                DeltaContent = choice.Delta?.Content,
                IsDone = choice.FinishReason is not null,
                PromptTokens = chunk?.Usage?.PromptTokens,
                CompletionTokens = chunk?.Usage?.CompletionTokens,
            };
        }
    }

    private sealed class ChatCompletionChunkDto
    {
        [JsonPropertyName("choices")]
        public List<ChoiceDto>? Choices { get; set; }

        [JsonPropertyName("usage")]
        public UsageDto? Usage { get; set; }
    }

    private sealed class ChoiceDto
    {
        [JsonPropertyName("delta")]
        public DeltaDto? Delta { get; set; }

        [JsonPropertyName("finish_reason")]
        public string? FinishReason { get; set; }
    }

    private sealed class DeltaDto
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }

    private sealed class UsageDto
    {
        [JsonPropertyName("prompt_tokens")]
        public int? PromptTokens { get; set; }

        [JsonPropertyName("completion_tokens")]
        public int? CompletionTokens { get; set; }
    }

    private sealed class ModelsResponse
    {
        [JsonPropertyName("data")]
        public List<ModelDto> Data { get; set; } = [];
    }

    private sealed class ModelDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("meta")]
        public ModelMetaDto? Meta { get; set; }
    }

    private sealed class ModelMetaDto
    {
        [JsonPropertyName("n_params")]
        public long? NParams { get; set; }

        [JsonPropertyName("ftype")]
        public string? FType { get; set; }

        [JsonPropertyName("n_ctx")]
        public int? NCtx { get; set; }

        [JsonPropertyName("size")]
        public long? Size { get; set; }
    }
}
