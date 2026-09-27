using Sweeply.Services;

namespace Sweeply.Tasks;

/// <summary>
/// Bir veya birden çok uygulamanın önbelleklerini temizleyen genel görev. Tüm önbellek görevleri
/// (<see cref="Catalog"/>) bunun örnekleridir; analiz, önizleme, iptal, ilerleme ve "açık uygulamayı atla"
/// davranışı böylece her görevde aynıdır.
/// </summary>
public sealed record CacheTask : ICleanupTask
{
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required TaskGroup Group { get; init; }
    public required Func<IEnumerable<CacheApp>> Apps { get; init; }
    public TaskKind Kind => TaskKind.Disk;
    public bool EnabledByDefault { get; init; } = true;
    public bool RequiresAdmin { get; init; }

    /// <summary>Her zaman gösterilen uyarı (ör. Spotify'da indirilen şarkıların da silinebileceği).</summary>
    public string? Warning { get; init; }

    /// <summary>Temizlikten önce/sonra çalışır (ör. Windows Update servisini durdurup yeniden başlatmak). Sonraki adım iptalde de çalışır.</summary>
    public Func<CancellationToken, Task>? Before { get; init; }
    public Func<Task>? After { get; init; }

    // Testlerde değiştirilebilen bağımlılıklar.
    public Func<string, bool> IsRunning { get; init; } = name => CommandRunner.CountProcesses(name) > 0;
    public Func<bool> IsAdmin { get; init; } = () => Elevation.IsAdmin;
    public Func<string, string?> FindTool { get; init; } = FindFirstTool;

    /// <summary>"pnpm.cmd|pnpm.exe" gibi alternatiflerden PATH'te ilk bulunanı döndürür.</summary>
    static string? FindFirstTool(string names) =>
        names.Split('|').Select(CommandRunner.FindOnPath).FirstOrDefault(p => p != null);

    bool Exists(CacheSource source) => source switch
    {
        FolderSource f => Directory.Exists(f.Path),
        CommandSource c => FindTool(c.Executable) != null || (c.FolderFallback && c.MeasurePaths.Any(Directory.Exists)),
        _ => false,
    };

    static long Measure(CacheSource source) => source switch
    {
        FolderSource f => FileCleaner.Measure(f.Path, f.MinAge),
        CommandSource c => c.MeasurePaths.Sum(p => FileCleaner.Measure(p)),
        _ => 0,
    };

    (List<CacheApp> Closed, List<CacheApp> Open, int Installed) Split()
    {
        var installed = Apps().Where(a => a.Sources.Any(Exists)).ToList();
        var open = installed.Where(a => a.Processes.Any(IsRunning)).ToList();
        return (installed.Except(open).ToList(), open, installed.Count);
    }

    static string Names(IEnumerable<CacheApp> apps) => string.Join(", ", apps.Select(a => a.Name));

    public Task<Analysis> AnalyzeAsync(CancellationToken ct) => Task.Run(() =>
    {
        var (closed, open, installed) = Split();
        if (installed == 0) return new Analysis(false, 0, "Bu bilgisayarda bulunamadı.");

        long bytes = closed.SelectMany(a => a.Sources.Where(Exists)).Sum(Measure);
        var notes = new List<string>();
        if (open.Count > 0) notes.Add($"{Names(open)} açık; kapatınca temizlenebilir.");
        if (Warning != null) notes.Add(Warning);
        string? note = notes.Count > 0 ? string.Join(" ", notes) : null;

        if (RequiresAdmin && !IsAdmin())
            return new Analysis(false, bytes, "Yönetici izni gerekir; üstteki \"Yönetici olarak aç\" ile yeniden başlatın.");
        if (closed.Count == 0) return new Analysis(false, 0, note);
        return new Analysis(true, bytes, note);
    }, ct);

    public Task<Preview> PreviewAsync(CancellationToken ct) => Task.Run(() =>
    {
        var (closed, open, _) = Split();
        var entries = new List<PreviewEntry>();
        foreach (var source in closed.SelectMany(a => a.Sources.Where(Exists)))
        {
            ct.ThrowIfCancellationRequested();
            switch (source)
            {
                case FolderSource f:
                    entries.AddRange(FileCleaner.Preview(f.Path, f.MinAge));
                    break;
                case CommandSource c when FindTool(c.Executable) != null:
                    entries.Add(new PreviewEntry(c.Display, Measure(c), 0, IsCommand: true));
                    break;
                case CommandSource c:
                    foreach (var path in c.MeasurePaths) entries.AddRange(FileCleaner.Preview(path));
                    break;
            }
        }
        string? note = open.Count > 0 ? $"{Names(open)} açık olduğu için atlanacak." : null;
        return new Preview(entries, note);
    }, ct);

    public async Task<TaskResult> RunAsync(IProgress<CleanProgress>? progress, CancellationToken ct)
    {
        if (RequiresAdmin && !IsAdmin()) return new TaskResult(0, "Yönetici izni gerekir");

        var (closed, open, _) = Split();
        var total = new CleanStats();
        var failed = new List<string>();

        if (Before != null) await Before(ct);
        try
        {
            foreach (var app in closed)
            {
                foreach (var source in app.Sources.Where(Exists))
                {
                    if (ct.IsCancellationRequested)
                    {
                        total.Cancelled = true;
                        break;
                    }

                    var offset = new OffsetProgress(progress, total.Deleted, total.Freed);
                    switch (source)
                    {
                        case FolderSource f:
                            total.Add(await Task.Run(() => FileCleaner.Clean(f.Path, f.MinAge, offset, ct)));
                            break;

                        case CommandSource c when FindTool(c.Executable) is { } tool:
                            long before = await Task.Run(() => Measure(c));
                            var (exit, _) = await RunToolAsync(tool, c, ct);
                            long freed = Math.Max(0, before - await Task.Run(() => Measure(c)));
                            total.Freed += freed;
                            offset.Report(new CleanProgress(0, freed));
                            if (ct.IsCancellationRequested) total.Cancelled = true;
                            else if (exit != 0) failed.Add(app.Name);
                            break;

                        case CommandSource c:
                            foreach (var path in c.MeasurePaths)
                                total.Add(await Task.Run(() => FileCleaner.Clean(path, default, new OffsetProgress(progress, total.Deleted, total.Freed), ct)));
                            break;
                    }
                }
            }
        }
        finally
        {
            if (After != null) await After();
        }

        string message = FileCleaner.Describe(total);
        if (failed.Count > 0) message += $" · {Names(closed.Where(a => failed.Contains(a.Name)))} temizleme komutu hata verdi";
        if (open.Count > 0) message += $" · {Names(open)} açık olduğu için atlandı";
        return new TaskResult(total.Freed, message);
    }

    static Task<(int ExitCode, string Output)> RunToolAsync(string tool, CommandSource c, CancellationToken ct)
    {
        var timeout = c.Timeout ?? TimeSpan.FromMinutes(5);
        // .cmd/.bat betikleri (npm, yarn, pnpm) doğrudan değil cmd.exe üzerinden çalıştırılır.
        return tool.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || tool.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)
            ? CommandRunner.RunAsync("cmd.exe", $"/c \"\"{tool}\" {c.Arguments}\"", timeout, ct)
            : CommandRunner.RunAsync(tool, c.Arguments, timeout, ct);
    }

    /// <summary>Bir alt adımın ilerlemesini görevin o ana kadarki toplamına ekleyerek iletir.</summary>
    sealed class OffsetProgress(IProgress<CleanProgress>? inner, int files, long bytes) : IProgress<CleanProgress>
    {
        public void Report(CleanProgress value) => inner?.Report(new CleanProgress(files + value.Files, bytes + value.Bytes));
    }
}
