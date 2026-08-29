using System.Net;
using System.Net.Http.Headers;
using LocalIA.Core.HuggingFace;
using Microsoft.Extensions.Logging;

namespace LocalIA.Infrastructure.HuggingFace;

public sealed class GgufDownloader(IHttpClientFactory httpClientFactory, ILogger<GgufDownloader> logger) : IGgufDownloader
{
    private const string HttpClientName = "HuggingFaceDownload";

    public async Task<string> DownloadAsync(string url, string destinationPath, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var partPath = destinationPath + ".part";
        var existingBytes = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;

        var client = httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (existingBytes > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existingBytes, null);
        }

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var isResuming = existingBytes > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (existingBytes > 0 && !isResuming)
        {
            logger.LogInformation("Le serveur ne supporte pas la reprise pour {Url}, redémarrage du téléchargement", url);
            existingBytes = 0;
        }

        var totalBytes = response.Content.Headers.ContentLength is { } contentLength
            ? contentLength + existingBytes
            : (long?)null;

        await using (var httpStream = await response.Content.ReadAsStreamAsync(ct))
        await using (var fileStream = new FileStream(
            partPath, isResuming ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            var buffer = new byte[81920];
            var totalReceived = isResuming ? existingBytes : 0L;
            int bytesRead;
            while ((bytesRead = await httpStream.ReadAsync(buffer, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                totalReceived += bytesRead;
                progress?.Report(new DownloadProgress(totalReceived, totalBytes));
            }
        }

        File.Move(partPath, destinationPath, overwrite: true);
        return destinationPath;
    }
}
