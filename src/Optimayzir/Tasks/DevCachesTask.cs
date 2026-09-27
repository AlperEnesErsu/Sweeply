using Optimayzir.Services;

namespace Optimayzir.Tasks;

/// <summary>npm ve pip indirme önbellekleri. Paketler gerektiğinde internetten tekrar indirilir; projelere etkisi yoktur.</summary>
public sealed class DevCachesTask : ICleanupTask
{
    static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    static readonly string RoamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    static string? NpmCacheDir =>
        new[] { Path.Combine(LocalAppData, "npm-cache"), Path.Combine(RoamingAppData, "npm-cache") }
            .FirstOrDefault(Directory.Exists);

    static string PipCacheDir => Path.Combine(LocalAppData, @"pip\cache");

    public string Title => "Geliştirici önbellekleri (npm, pip)";
    public string Description => "İndirilmiş paket önbellekleri. Projelerinize dokunmaz; paketler gerekince tekrar indirilir.";
    public TaskKind Kind => TaskKind.Disk;
    public bool EnabledByDefault => true;

    static long MeasureAll()
    {
        long total = FileCleaner.Measure(PipCacheDir);
        if (NpmCacheDir is { } npm) total += FileCleaner.Measure(Path.Combine(npm, "_cacache"));
        return total;
    }

    public Task<Analysis> AnalyzeAsync(CancellationToken ct) => Task.Run(() =>
    {
        if (NpmCacheDir == null && !Directory.Exists(PipCacheDir))
            return new Analysis(false, 0, "npm veya pip önbelleği bulunamadı.");
        return new Analysis(true, MeasureAll());
    }, ct);

    public async Task<TaskResult> RunAsync(CancellationToken ct)
    {
        long before = await Task.Run(MeasureAll, ct);

        // npm önbelleğini kendi komutuyla temizlemek, dizin yapısını npm'in beklediği hâlde bırakır.
        var npm = CommandRunner.FindOnPath("npm.cmd");
        if (npm != null)
            await CommandRunner.RunAsync("cmd.exe", $"/c \"\"{npm}\" cache clean --force\"", TimeSpan.FromMinutes(5), ct);
        else if (NpmCacheDir is { } npmDir)
            await Task.Run(() => FileCleaner.Clean(Path.Combine(npmDir, "_cacache")), ct);

        await Task.Run(() => FileCleaner.Clean(PipCacheDir), ct);

        long freed = Math.Max(0, before - await Task.Run(MeasureAll, ct));
        return new TaskResult(freed, $"{Format.Bytes(freed)} silindi");
    }
}
