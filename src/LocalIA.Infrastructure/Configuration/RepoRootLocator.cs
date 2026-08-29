namespace LocalIA.Infrastructure.Configuration;

/// <summary>
/// Recherche best-effort de la racine du dépôt legacy (config/config.json + scripts/) en
/// remontant depuis le dossier de l'exécutable — ne fonctionne que si l'app tourne depuis le
/// dépôt (ex. via `dotnet run` en développement) ; sinon l'import legacy est simplement ignoré.
/// </summary>
internal static class RepoRootLocator
{
    public static string? TryFind(string startDirectory, int maxLevelsUp = 8)
    {
        var directory = new DirectoryInfo(startDirectory);
        for (var i = 0; i < maxLevelsUp && directory is not null; i++)
        {
            var candidateConfig = Path.Combine(directory.FullName, "config", "config.json");
            var candidateScripts = Path.Combine(directory.FullName, "scripts");
            if (File.Exists(candidateConfig) && Directory.Exists(candidateScripts))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
