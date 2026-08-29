using LocalIA.Core.Abstractions;

namespace LocalIA.Infrastructure.Engines;

public sealed class AutostartService : IAutostartService
{
    private static readonly string ShortcutPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup), "LOCAL-IA.lnk");

    public Task<bool> IsEnabledAsync(CancellationToken ct = default) => Task.FromResult(File.Exists(ShortcutPath));

    public Task EnableAsync(CancellationToken ct = default)
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            throw new InvalidOperationException("Impossible de déterminer le chemin de l'exécutable courant.");
        }

        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("WScript.Shell est indisponible sur cette machine.");

        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic shortcut = shell.CreateShortcut(ShortcutPath);
            shortcut.TargetPath = exePath;
            shortcut.WorkingDirectory = Path.GetDirectoryName(exePath);
            shortcut.Save();
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
        }

        return Task.CompletedTask;
    }

    public Task DisableAsync(CancellationToken ct = default)
    {
        if (File.Exists(ShortcutPath))
        {
            File.Delete(ShortcutPath);
        }

        return Task.CompletedTask;
    }
}
