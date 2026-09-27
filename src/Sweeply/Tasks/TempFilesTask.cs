using Sweeply.Services;

namespace Sweeply.Tasks;

public sealed class TempFilesTask : ICleanupTask
{
    // Yeni dosyalar çalışan bir kurulumun veya programın olabilir; onlara dokunulmaz.
    internal static readonly TimeSpan MinAge = TimeSpan.FromHours(24);

    readonly string[] _folders;

    public TempFilesTask() : this(
        Path.GetTempPath(),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"))
    {
    }

    internal TempFilesTask(params string[] folders) => _folders = folders;

    public string Title => "Geçici dosyalar";
    public string Description => "Kullanıcı ve Windows Temp klasörlerindeki 24 saatten eski dosyalar. Kullanımdaki dosyalar atlanır.";
    public TaskKind Kind => TaskKind.Disk;
    public bool EnabledByDefault => true;

    public Task<Analysis> AnalyzeAsync(CancellationToken ct) =>
        Task.Run(() => new Analysis(true, _folders.Sum(f => FileCleaner.Measure(f, MinAge))), ct);

    public Task<TaskResult> RunAsync(CancellationToken ct) => Task.Run(() =>
    {
        var total = new CleanStats();
        foreach (var folder in _folders) total.Add(FileCleaner.Clean(folder, MinAge));
        return new TaskResult(total.Freed, FileCleaner.Describe(total));
    }, ct);
}
