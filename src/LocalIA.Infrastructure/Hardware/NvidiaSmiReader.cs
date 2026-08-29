using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace LocalIA.Infrastructure.Hardware;

public sealed record NvidiaGpuStats(string Name, long UsedMemoryMiB, long TotalMemoryMiB, double UtilizationPercent, double TemperatureCelsius);

public sealed class NvidiaSmiReader
{
    public async Task<NvidiaGpuStats?> ReadAsync(CancellationToken ct = default)
    {
        try
        {
            var startInfo = new ProcessStartInfo("nvidia-smi")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                ArgumentList =
                {
                    "--query-gpu=name,memory.used,memory.total,utilization.gpu,temperature.gpu",
                    "--format=csv,noheader,nounits",
                },
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var output = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            var firstLine = output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
            if (firstLine is null)
            {
                return null;
            }

            var parts = firstLine.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length < 5)
            {
                return null;
            }

            return new NvidiaGpuStats(
                Name: parts[0],
                UsedMemoryMiB: long.Parse(parts[1], CultureInfo.InvariantCulture),
                TotalMemoryMiB: long.Parse(parts[2], CultureInfo.InvariantCulture),
                UtilizationPercent: double.Parse(parts[3], CultureInfo.InvariantCulture),
                TemperatureCelsius: double.Parse(parts[4], CultureInfo.InvariantCulture));
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}
