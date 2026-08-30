using System.IO.Compression;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using LocalIA.Core.Abstractions;
using LocalIA.Core.HuggingFace;
using LocalIA.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;

namespace LocalIA.Infrastructure.Engines;

public sealed class EngineInstaller(IGgufDownloader downloader, IHttpClientFactory httpClientFactory, ILogger<EngineInstaller> logger) : IEngineInstaller
{
    // Même nom que celui déjà enregistré pour IGgufDownloader (timeout infini, User-Agent défini —
    // l'API GitHub rejette les requêtes sans User-Agent) : un seul client HTTP pour tous les
    // téléchargements de fichiers volumineux, pas une seconde config qui pourrait diverger.
    private const string DownloadHttpClientName = "HuggingFaceDownload";

    private const string OllamaInstallerUrl = "https://ollama.com/download/OllamaSetup.exe";

    // Même critère de sélection que scripts/Install-LlamaCpp.ps1 (toolkit legacy) : la première
    // release qui propose à la fois les binaires principaux ET le runtime CUDA pour CUDA 12.4/x64.
    private static readonly Regex MainAssetPattern = new(@"^llama-.*-bin-win-cuda-12\.4-x64\.zip$", RegexOptions.IgnoreCase);
    private static readonly Regex CudaRuntimeAssetPattern = new(@"^cudart-llama-bin-win-cuda-12\.4-x64\.zip$", RegexOptions.IgnoreCase);

    public async Task LaunchOllamaInstallerAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var installerPath = Path.Combine(Path.GetTempPath(), "OllamaSetup.exe");
        progress?.Report("Téléchargement de l'installeur Ollama…");
        await downloader.DownloadAsync(OllamaInstallerUrl, installerPath, ConvertProgress(progress), ct);

        progress?.Report("Lancement de l'installeur Ollama…");
        // UseShellExecute : laisse Windows gérer l'élévation UAC et affiche le véritable installeur
        // (licence, emplacement) — jamais d'installation silencieuse décidée à la place de l'utilisateur.
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(installerPath) { UseShellExecute = true });
        _ = process;
    }

    public async Task<string> InstallLlamaCppAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var repoRoot = RepoRootLocator.TryFind(AppContext.BaseDirectory);
        var targetDirectory = repoRoot is not null
            ? Path.Combine(repoRoot, "bin", "llama-cpp")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalIA", "llama-cpp");
        Directory.CreateDirectory(targetDirectory);
        var tempDir = Path.Combine(Path.GetTempPath(), "local-ia-llama-cpp-download");
        Directory.CreateDirectory(tempDir);

        progress?.Report("Recherche de la dernière version CUDA 12.4 sur GitHub…");
        var client = httpClientFactory.CreateClient(DownloadHttpClientName);
        var releases = await client.GetFromJsonAsync<List<GitHubReleaseDto>>(
            "https://api.github.com/repos/ggml-org/llama.cpp/releases?per_page=10", ct)
            ?? throw new InvalidOperationException("Impossible de contacter l'API GitHub pour llama.cpp.");

        GitHubAssetDto? mainAsset = null;
        GitHubAssetDto? cudaAsset = null;
        string? releaseTag = null;
        foreach (var release in releases)
        {
            var main = release.Assets.FirstOrDefault(a => MainAssetPattern.IsMatch(a.Name));
            var cuda = release.Assets.FirstOrDefault(a => CudaRuntimeAssetPattern.IsMatch(a.Name));
            if (main is not null && cuda is not null)
            {
                mainAsset = main;
                cudaAsset = cuda;
                releaseTag = release.TagName;
                break;
            }
        }

        if (mainAsset is null || cudaAsset is null)
        {
            throw new InvalidOperationException("Aucune release llama.cpp avec binaires CUDA 12.4 (x64) trouvée sur GitHub.");
        }

        logger.LogInformation("Installation de llama.cpp {Tag} vers {TargetDirectory}", releaseTag, targetDirectory);

        var mainZipPath = Path.Combine(tempDir, mainAsset.Name);
        var cudaZipPath = Path.Combine(tempDir, cudaAsset.Name);
        try
        {
            progress?.Report($"Téléchargement de {mainAsset.Name}…");
            await downloader.DownloadAsync(mainAsset.BrowserDownloadUrl, mainZipPath, ConvertProgress(progress), ct);

            progress?.Report($"Téléchargement de {cudaAsset.Name}…");
            await downloader.DownloadAsync(cudaAsset.BrowserDownloadUrl, cudaZipPath, ConvertProgress(progress), ct);

            progress?.Report("Extraction des fichiers…");
            ZipFile.ExtractToDirectory(mainZipPath, targetDirectory, overwriteFiles: true);
            ZipFile.ExtractToDirectory(cudaZipPath, targetDirectory, overwriteFiles: true);
        }
        finally
        {
            File.Delete(mainZipPath);
            File.Delete(cudaZipPath);
        }

        var serverExePath = Path.Combine(targetDirectory, "llama-server.exe");
        if (!File.Exists(serverExePath))
        {
            throw new InvalidOperationException($"llama-server.exe introuvable après extraction dans {targetDirectory}.");
        }

        progress?.Report($"llama.cpp {releaseTag} installé.");
        return serverExePath;
    }

    private static IProgress<DownloadProgress>? ConvertProgress(IProgress<string>? progress)
    {
        if (progress is null)
        {
            return null;
        }

        return new Progress<DownloadProgress>(p => progress.Report(
            p.PercentComplete is { } percent ? $"{percent:0}%" : $"{p.BytesReceived / 1024 / 1024} Mo"));
    }

    private sealed class GitHubReleaseDto
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = "";

        [JsonPropertyName("assets")]
        public List<GitHubAssetDto> Assets { get; set; } = [];
    }

    private sealed class GitHubAssetDto
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = "";
    }
}
