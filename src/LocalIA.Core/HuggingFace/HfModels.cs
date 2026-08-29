namespace LocalIA.Core.HuggingFace;

public sealed class HfModelSummary
{
    public required string Id { get; init; }
    public int Downloads { get; init; }
    public int Likes { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];

    public string Author => Id.Contains('/') ? Id[..Id.IndexOf('/')] : "";
    public string RepoName => Id.Contains('/') ? Id[(Id.IndexOf('/') + 1)..] : Id;

    public override string ToString() => Id;
}

public sealed class HfRepoFile
{
    public required string Path { get; init; }
    public long? SizeBytes { get; init; }
    public string? QuantizationLabel { get; init; }
    public GgufFileRole Role { get; init; }

    public string FileName => System.IO.Path.GetFileName(Path);
}

public interface IHuggingFaceClient
{
    Task<IReadOnlyList<HfModelSummary>> SearchModelsAsync(string query, int limit = 20, CancellationToken ct = default);
    Task<IReadOnlyList<HfRepoFile>> ListRepoFilesAsync(string repoId, string revision = "main", CancellationToken ct = default);
}
