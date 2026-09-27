using Sweeply.Services;

namespace Sweeply.Tasks;

public sealed class CrashDumpsTask : ICleanupTask
{
    readonly string[] _folders;

    public CrashDumpsTask() : this(DefaultFolders())
    {
    }

    internal CrashDumpsTask(params string[] folders) => _folders = folders;

    static string[] DefaultFolders()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return
        [
            Path.Combine(local, "CrashDumps"),
            Path.Combine(local, @"Microsoft\Windows\WER\ReportArchive"),
            Path.Combine(local, @"Microsoft\Windows\WER\ReportQueue"),
        ];
    }

    public string Title => "Çökme dökümleri ve hata raporları";
    public string Description => "Çöken programların bıraktığı döküm dosyaları ve gönderilmiş Windows hata raporları.";
    public TaskKind Kind => TaskKind.Disk;
    public bool EnabledByDefault => true;

    public Task<Analysis> AnalyzeAsync(CancellationToken ct) =>
        Task.Run(() => new Analysis(true, _folders.Sum(f => FileCleaner.Measure(f))), ct);

    public Task<TaskResult> RunAsync(CancellationToken ct) => Task.Run(() =>
    {
        var total = new CleanStats();
        foreach (var folder in _folders) total.Add(FileCleaner.Clean(folder));
        return new TaskResult(total.Freed, FileCleaner.Describe(total));
    }, ct);
}
