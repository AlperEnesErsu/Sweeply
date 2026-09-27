using Optimayzir.Services;

namespace Optimayzir.Tasks;

public sealed class CrashDumpsTask : ICleanupTask
{
    static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    static IEnumerable<string> Folders =>
    [
        Path.Combine(LocalAppData, "CrashDumps"),
        Path.Combine(LocalAppData, @"Microsoft\Windows\WER\ReportArchive"),
        Path.Combine(LocalAppData, @"Microsoft\Windows\WER\ReportQueue"),
    ];

    public string Title => "Çökme dökümleri ve hata raporları";
    public string Description => "Çöken programların bıraktığı döküm dosyaları ve gönderilmiş Windows hata raporları.";
    public TaskKind Kind => TaskKind.Disk;
    public bool EnabledByDefault => true;

    public Task<Analysis> AnalyzeAsync(CancellationToken ct) =>
        Task.Run(() => new Analysis(true, Folders.Sum(f => FileCleaner.Measure(f))), ct);

    public Task<TaskResult> RunAsync(CancellationToken ct) => Task.Run(() =>
    {
        long freed = Folders.Sum(f => FileCleaner.Clean(f).Freed);
        return new TaskResult(freed, $"{Format.Bytes(freed)} silindi");
    }, ct);
}
