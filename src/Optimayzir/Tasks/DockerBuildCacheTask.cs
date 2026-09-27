using System.Text.RegularExpressions;
using Optimayzir.Services;

namespace Optimayzir.Tasks;

/// <summary>
/// Sadece Docker build cache'ini temizler. İmajlar, konteynerler ve volume'lar (yani proje verileri) korunur;
/// tek etkisi bir sonraki build'in ilk seferde biraz daha uzun sürmesidir.
/// </summary>
public sealed class DockerBuildCacheTask : ICleanupTask
{
    public string Title => "Docker build cache";
    public string Description => "Build ara katmanları. İmajlara, konteynerlere ve volume'lara dokunmaz. Docker açıkken çalışır.";
    public TaskKind Kind => TaskKind.Disk;
    public bool EnabledByDefault => true;

    public async Task<Analysis> AnalyzeAsync(CancellationToken ct)
    {
        var docker = CommandRunner.FindOnPath("docker.exe");
        if (docker == null) return new Analysis(false, 0, "Docker yüklü değil.");

        var info = await CommandRunner.RunAsync(docker, "info --format {{.ServerVersion}}", TimeSpan.FromSeconds(15), ct);
        if (info.ExitCode != 0) return new Analysis(false, 0, "Docker çalışmıyor; açıkken tekrar analiz edin.");

        var df = await CommandRunner.RunAsync(docker, "system df --format \"{{.Type}}|{{.Reclaimable}}\"", TimeSpan.FromSeconds(30), ct);
        var line = df.Output.Split('\n').FirstOrDefault(l => l.StartsWith("Build Cache|", StringComparison.OrdinalIgnoreCase));
        long bytes = line == null ? -1 : Format.ParseDockerSize(line.Split('|')[1]);
        return new Analysis(true, bytes, "Alan Docker'ın sanal diskinde açılır; Windows'a geri vermek için README'deki sıkıştırma adımına bakın.");
    }

    public async Task<TaskResult> RunAsync(CancellationToken ct)
    {
        var docker = CommandRunner.FindOnPath("docker.exe");
        if (docker == null) return new TaskResult(0, "Docker bulunamadı");

        var result = await CommandRunner.RunAsync(docker, "builder prune -a -f", TimeSpan.FromMinutes(10), ct);
        if (result.ExitCode != 0) return new TaskResult(0, "Docker komutu başarısız: " + result.Output.Trim());

        var total = Regex.Match(result.Output, @"Total:\s*(\S+)");
        long freed = total.Success ? Format.ParseDockerSize(total.Groups[1].Value) : 0;
        return new TaskResult(freed, $"{Format.Bytes(freed)} build cache silindi (Docker sanal diski içinde)");
    }
}
