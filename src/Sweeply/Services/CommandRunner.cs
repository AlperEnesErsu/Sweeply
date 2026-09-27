using System.Diagnostics;
using System.Text;

namespace Sweeply.Services;

public static class CommandRunner
{
    public static async Task<(int ExitCode, string Output)> RunAsync(string fileName, string arguments, TimeSpan timeout, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using var process = new Process { StartInfo = psi };
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return (-1, "zaman aşımı");
        }

        return (process.ExitCode, await stdout + await stderr);
    }

    /// <summary>PATH içinde bir çalıştırılabilir dosyayı arar (ör. "docker.exe", "npm.cmd").</summary>
    public static string? FindOnPath(string fileName)
    {
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var dir in dirs)
        {
            try
            {
                var full = Path.Combine(dir.Trim(), fileName);
                if (File.Exists(full)) return full;
            }
            catch { }
        }
        return null;
    }

    public static int CountProcesses(string name)
    {
        var processes = Process.GetProcessesByName(name);
        foreach (var p in processes) p.Dispose();
        return processes.Length;
    }
}
