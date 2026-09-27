using System.Diagnostics;
using Optimayzir.Services;

namespace Optimayzir.Tasks;

/// <summary>
/// WSL sanal makinesini (ve onu kullanan Docker Desktop'ı) kapatır. Genellikle birkaç GB RAM geri kazandırır,
/// ama çalışan konteynerleri de durdurduğu için varsayılan olarak kapalıdır.
/// </summary>
public sealed class WslShutdownTask : ICleanupTask
{
    public string Title => "WSL ve Docker'ı kapat";
    public string Description => "Kullanmıyorsanız WSL sanal makinesini ve Docker Desktop'ı kapatarak RAM'i geri alır. Çalışan konteynerler durur.";
    public TaskKind Kind => TaskKind.Ram;
    public bool EnabledByDefault => false;

    /// <summary>vmmem işlemlerinin kullandığı RAM. İşlem var ama belleği okunamıyorsa -1.</summary>
    static long VmMemory()
    {
        long total = 0;
        bool found = false, unreadable = false;
        foreach (var name in new[] { "vmmemWSL", "vmmem" })
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                found = true;
                try { total += p.WorkingSet64; }
                catch { unreadable = true; }
                finally { p.Dispose(); }
            }
        }
        if (!found) return 0;
        return unreadable && total == 0 ? -1 : total;
    }

    public Task<Analysis> AnalyzeAsync(CancellationToken ct) => Task.Run(() =>
    {
        long bytes = VmMemory();
        return bytes == 0
            ? new Analysis(false, 0, "WSL şu an çalışmıyor.")
            : new Analysis(true, bytes, "Çalışan Docker konteynerleri de durur.");
    }, ct);

    public async Task<TaskResult> RunAsync(CancellationToken ct)
    {
        long before = VmMemory();

        var docker = CommandRunner.FindOnPath("docker.exe");
        if (docker != null && CommandRunner.CountProcesses("Docker Desktop") > 0)
            await CommandRunner.RunAsync(docker, "desktop stop", TimeSpan.FromSeconds(90), ct);

        var shutdown = await CommandRunner.RunAsync("wsl.exe", "--shutdown", TimeSpan.FromSeconds(60), ct);
        if (shutdown.ExitCode != 0) return new TaskResult(0, "WSL kapatılamadı");

        await Task.Delay(TimeSpan.FromSeconds(2), ct);
        long freed = before > 0 ? Math.Max(0, before - Math.Max(0, VmMemory())) : 0;
        return new TaskResult(freed, freed > 0 ? $"{Format.Bytes(freed)} RAM boşaldı" : "WSL kapatıldı");
    }
}
