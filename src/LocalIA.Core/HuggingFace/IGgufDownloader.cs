namespace LocalIA.Core.HuggingFace;

public sealed record DownloadProgress(long BytesReceived, long? TotalBytes)
{
    public double? PercentComplete => TotalBytes is > 0 ? 100.0 * BytesReceived / TotalBytes.Value : null;
}

public interface IGgufDownloader
{
    /// <summary>
    /// Télécharge en flux avec reprise (fichier .part + en-tête Range si une tentative précédente
    /// a été interrompue). Retourne le chemin final une fois le téléchargement terminé.
    /// </summary>
    Task<string> DownloadAsync(string url, string destinationPath, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default);
}
