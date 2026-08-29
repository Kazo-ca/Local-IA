using System.Net.Http.Json;
using System.Text.Json.Serialization;
using LocalIA.Core.HuggingFace;

namespace LocalIA.Infrastructure.HuggingFace;

public sealed class HuggingFaceClient(HttpClient httpClient) : IHuggingFaceClient
{
    public async Task<IReadOnlyList<HfModelSummary>> SearchModelsAsync(string query, int limit = 20, CancellationToken ct = default)
    {
        var url = $"api/models?search={Uri.EscapeDataString(query)}&filter=gguf&sort=downloads&direction=-1&limit={limit}";
        try
        {
            var results = await httpClient.GetFromJsonAsync<List<SearchResultDto>>(url, ct);
            return results?
                .Select(r => new HfModelSummary
                {
                    Id = r.Id ?? r.ModelId ?? "",
                    Downloads = r.Downloads,
                    Likes = r.Likes,
                    Tags = r.Tags ?? [],
                })
                .Where(m => !string.IsNullOrEmpty(m.Id))
                .ToList() ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return [];
        }
    }

    public async Task<IReadOnlyList<HfRepoFile>> ListRepoFilesAsync(string repoId, string revision = "main", CancellationToken ct = default)
    {
        var url = $"api/models/{repoId}/tree/{revision}";
        try
        {
            var entries = await httpClient.GetFromJsonAsync<List<TreeEntryDto>>(url, ct);
            return entries?
                .Where(e => e.Type == "file" && e.Path.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
                .Select(e => new HfRepoFile
                {
                    Path = e.Path,
                    SizeBytes = e.Size ?? e.Lfs?.Size,
                    QuantizationLabel = GgufQuantizationParser.TryParse(e.Path),
                    Role = GgufFileRoleClassifier.Classify(e.Path),
                })
                .ToList() ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return [];
        }
    }

    private sealed class SearchResultDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("modelId")]
        public string? ModelId { get; set; }

        [JsonPropertyName("downloads")]
        public int Downloads { get; set; }

        [JsonPropertyName("likes")]
        public int Likes { get; set; }

        [JsonPropertyName("tags")]
        public List<string>? Tags { get; set; }
    }

    private sealed class TreeEntryDto
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "";

        [JsonPropertyName("path")]
        public string Path { get; set; } = "";

        [JsonPropertyName("size")]
        public long? Size { get; set; }

        [JsonPropertyName("lfs")]
        public LfsDto? Lfs { get; set; }
    }

    private sealed class LfsDto
    {
        [JsonPropertyName("size")]
        public long? Size { get; set; }
    }
}
