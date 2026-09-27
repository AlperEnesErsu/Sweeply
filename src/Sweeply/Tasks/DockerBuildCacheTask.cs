using System.Text.RegularExpressions;
using Sweeply.Services;

namespace Sweeply.Tasks;

/// <summary>
/// Sadece Docker build cache'ini temizler. İmajlar, konteynerler ve volume'lar (yani proje verileri) korunur;
/// tek etkisi bir sonraki build'in ilk seferde biraz daha uzun sürmesidir.
/// </summary>
public sealed class DockerBuildCacheTask : ICleanupTask
{
    public string Title => "Docker build cache";
    public string Description => "Build ara katmanları. İmajlara, konteynerlere ve volume'lara dokunmaz. Docker açıkken çalışır.";
    public TaskKind Kind => TaskKind.Disk;
    public TaskGroup Group => TaskGroup.Developer;
    public bool EnabledByDefault => true;

    public async Task<Analysis> AnalyzeAsync(CancellationToken ct)
    {
        var docker = CommandRunner.FindOnPath("docker.exe");
        if (docker == null) return new Analysis(false, 0, "Bu bilgisayarda bulunamadı.");

        var info = await CommandRunner.RunAsync(docker, "info --format {{.ServerVersion}}", TimeSpan.FromSeconds(15), ct);
        if (info.ExitCode != 0) return new Analysis(false, 0, "Docker çalışmıyor; açıkken tekrar tarayın.");

        return new Analysis(true, await ReclaimableAsync(docker, ct),
            "Alan Docker'ın sanal diskinde açılır; Windows'a geri vermek için README'deki sıkıştırma adımına bakın.");
    }

    static async Task<long> ReclaimableAsync(string docker, CancellationToken ct)
    {
        var df = await CommandRunner.RunAsync(docker, "system df --format \"{{.Type}}|{{.Reclaimable}}\"", TimeSpan.FromSeconds(30), ct);
        return df.ExitCode == 0 ? ParseReclaimableBuildCache(df.Output) : -1;
    }

    public async Task<Preview> PreviewAsync(CancellationToken ct)
    {
        var docker = CommandRunner.FindOnPath("docker.exe");
        long bytes = docker == null ? 0 : Math.Max(0, await ReclaimableAsync(docker, ct));
        return new Preview([new PreviewEntry("docker builder prune -a -f", bytes, 0, IsCommand: true)],
            "Sadece build ara katmanları silinir; imajlar, konteynerler ve volume'lar korunur.");
    }

    public async Task<TaskResult> RunAsync(IProgress<CleanProgress>? progress, CancellationToken ct)
    {
        var docker = CommandRunner.FindOnPath("docker.exe");
        if (docker == null) return new TaskResult(0, "Docker bulunamadı");

        var result = await CommandRunner.RunAsync(docker, "builder prune -a -f", TimeSpan.FromMinutes(10), ct);
        if (ct.IsCancellationRequested) return new TaskResult(0, "Durduruldu");
        if (result.ExitCode != 0) return new TaskResult(0, "Docker komutu başarısız: " + result.Output.Trim());

        long freed = ParsePruneTotal(result.Output);
        return new TaskResult(freed, freed > 0
            ? $"{Format.Bytes(freed)} build cache silindi (Docker sanal diski içinde)"
            : "Build cache zaten boştu");
    }

    /// <summary>`docker system df --format "{{.Type}}|{{.Reclaimable}}"` çıktısından Build Cache satırını okur; bulunamazsa -1.</summary>
    internal static long ParseReclaimableBuildCache(string output)
    {
        var line = output
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(l => l.StartsWith("Build Cache|", StringComparison.OrdinalIgnoreCase));
        return line == null ? -1 : Format.ParseDockerSize(line["Build Cache|".Length..]);
    }

    /// <summary>`docker builder prune` çıktısının sonundaki "Total: 39.86GB" satırını okur; yoksa 0.</summary>
    internal static long ParsePruneTotal(string output)
    {
        var match = Regex.Match(output, @"Total:\s*(\S+)");
        return match.Success ? Format.ParseDockerSize(match.Groups[1].Value) : 0;
    }
}
