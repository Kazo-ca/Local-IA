using System.Diagnostics;

namespace LocalIA.Infrastructure.Engines;

internal static class ProcessLookup
{
    public static int? FindFirstIdByNamePrefix(string processNamePrefix)
    {
        var processes = Process.GetProcesses();
        try
        {
            foreach (var process in processes)
            {
                if (process.ProcessName.StartsWith(processNamePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    return process.Id;
                }
            }

            return null;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    public static IReadOnlyList<int> FindAllIdsByNamePrefix(string processNamePrefix)
    {
        var processes = Process.GetProcesses();
        try
        {
            return processes
                .Where(p => p.ProcessName.StartsWith(processNamePrefix, StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Id)
                .ToList();
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    /// <summary>Partagé entre OllamaProcessManager et LlamaCppProcessManager (même comportement d'arrêt pour les deux moteurs).</summary>
    public static void TryKill(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // déjà arrêté entre-temps
        }
    }
}
