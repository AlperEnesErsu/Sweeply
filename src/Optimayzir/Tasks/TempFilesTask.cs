using Optimayzir.Services;

namespace Optimayzir.Tasks;

public sealed class TempFilesTask : ICleanupTask
{
    // Yeni dosyalar çalışan bir kurulumun veya programın olabilir; onlara dokunulmaz.
    static readonly TimeSpan MinAge = TimeSpan.FromHours(24);

    static IEnumerable<string> Folders =>
    [
        Path.GetTempPath(),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"),
    ];

    public string Title => "Geçici dosyalar";
    public string Description => "Kullanıcı ve Windows Temp klasörlerindeki 24 saatten eski dosyalar. Kullanımdaki dosyalar atlanır.";
    public TaskKind Kind => TaskKind.Disk;
    public bool EnabledByDefault => true;

    public Task<Analysis> AnalyzeAsync(CancellationToken ct) =>
        Task.Run(() => new Analysis(true, Folders.Sum(f => FileCleaner.Measure(f, MinAge))), ct);

    public Task<TaskResult> RunAsync(CancellationToken ct) => Task.Run(() =>
    {
        long freed = 0;
        int skipped = 0;
        foreach (var folder in Folders)
        {
            var stats = FileCleaner.Clean(folder, MinAge);
            freed += stats.Freed;
            skipped += stats.Skipped;
        }
        string message = $"{Format.Bytes(freed)} silindi";
        if (skipped > 0) message += $" · {skipped} dosya kullanımda veya yeni olduğu için atlandı";
        return new TaskResult(freed, message);
    }, ct);
}
