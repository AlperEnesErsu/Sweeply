using Sweeply.Models;
using Sweeply.Services;
using Sweeply.Tasks;

namespace Sweeply.Tests;

public class RunLogTests
{
    sealed class StubTask(string title, bool requiresAdmin = false) : ICleanupTask
    {
        public string Title => title;
        public string Description => "";
        public TaskKind Kind => TaskKind.Disk;
        public TaskGroup Group => TaskGroup.System;
        public bool EnabledByDefault => true;
        public bool RequiresAdmin => requiresAdmin;
        public Task<Analysis> AnalyzeAsync(CancellationToken ct) => Task.FromResult(new Analysis(true, 0));
        public Task<Preview> PreviewAsync(CancellationToken ct) => Task.FromResult(Preview.Empty);
        public Task<TaskResult> RunAsync(IProgress<CleanProgress>? progress, CancellationToken ct) => Task.FromResult(new TaskResult(0, ""));
    }

    static CleanupItem Item(string title, bool enabled, ItemState state, string? status = null, string? note = null, bool available = true) =>
        new(new StubTask(title)) { IsEnabled = enabled, State = state, Status = status, Note = note, IsAvailable = available };

    [Fact]
    public void ScheduledRunLog_RecordsOnlyEnabledTasksWithOutcomes()
    {
        var items = new[]
        {
            Item("Geçici dosyalar", true, ItemState.Done, "1 GB silindi"),
            Item("Spotify", false, ItemState.Idle),
            Item("Windows Update", true, ItemState.Idle, note: "Yönetici izni gerekir", available: false),
            Item("Docker", true, ItemState.Failed, "Docker komutu başarısız"),
            Item("Tarayıcılar", true, ItemState.Skipped, "Durduruldu"),
        };

        var log = Summaries.ScheduledRunLog(items, 1024, 0, cancelled: true, new DateTime(2026, 11, 1, 10, 0, 0));

        Assert.Equal(["Geçici dosyalar", "Windows Update", "Docker", "Tarayıcılar"], log.Entries.Select(e => e.Title));
        Assert.Equal([RunOutcome.Done, RunOutcome.NotRun, RunOutcome.Failed, RunOutcome.Skipped], log.Entries.Select(e => e.Outcome));
        Assert.Equal("Yönetici izni gerekir", log.Entries[1].Message);
        Assert.Equal(1024, log.DiskFreed);
        Assert.True(log.Cancelled);
        Assert.False(log.Seen);
    }

    [Fact]
    public void ScheduledRunLog_NotRunWithoutNote_GetsGenericReason()
    {
        var log = Summaries.ScheduledRunLog([Item("X", true, ItemState.Idle, available: false)], 0, 0, false, DateTime.Now);
        Assert.False(string.IsNullOrEmpty(log.Entries[0].Message));
    }

    [Fact]
    public void RunLogSummary_CountsUnfinishedTasksInTitle()
    {
        var log = new RunLog
        {
            Time = new DateTime(2026, 11, 1, 10, 0, 0),
            DiskFreed = 2L * 1024 * 1024 * 1024,
            Entries =
            [
                new("A", RunOutcome.Done, "tamam"),
                new("B", RunOutcome.NotRun, "Yönetici izni gerekir"),
                new("C", RunOutcome.Failed, "hata"),
            ],
        };

        var (title, message, rows) = Summaries.RunLogSummary(log);

        Assert.Contains("2 görev tamamlanmadı", title);
        Assert.Contains("disk alanı açıldı", message);
        Assert.Equal(3, rows.Count);
        Assert.Equal("Çalışmadı", rows[1].Right);
        Assert.Equal("Yönetici izni gerekir", rows[1].Secondary);
    }

    [Fact]
    public void RunLogSummary_AllDone_PlainTitle()
    {
        var (title, message, _) = Summaries.RunLogSummary(new RunLog { Entries = [new("A", RunOutcome.Done, null)] });
        Assert.Equal("Son otomatik temizlik", title);
        Assert.Contains("disk alanı açılmadı", message);
    }

    [Fact]
    public void RunLogSummary_NoEntries_ShowsPlaceholderRow()
    {
        var (_, _, rows) = Summaries.RunLogSummary(new RunLog());
        Assert.Single(rows);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        using var dir = new TempDir();
        var file = Path.Combine(dir.Path, "sub", "last-run.json");
        var log = new RunLog
        {
            Time = new DateTime(2026, 11, 1, 10, 0, 0),
            DiskFreed = 5,
            RamFreed = 7,
            Entries = [new("A", RunOutcome.Failed, "hata")],
        };

        Assert.True(log.Save(file));
        var loaded = RunLog.Load(file)!;

        Assert.Equal(log.Time, loaded.Time);
        Assert.Equal(5, loaded.DiskFreed);
        Assert.Equal(7, loaded.RamFreed);
        Assert.Equal(log.Entries, loaded.Entries);
        Assert.False(loaded.Seen);
    }

    [Fact]
    public void Load_MissingOrCorrupt_ReturnsNull()
    {
        using var dir = new TempDir();
        Assert.Null(RunLog.Load(Path.Combine(dir.Path, "none.json")));

        var file = dir.File("last-run.json", 0);
        File.WriteAllText(file, "bozuk");
        Assert.Null(RunLog.Load(file));
    }

    [Fact]
    public void Load_NullEntries_BecomesEmpty()
    {
        using var dir = new TempDir();
        var file = dir.File("last-run.json", 0);
        File.WriteAllText(file, """{ "Entries": null }""");
        Assert.Empty(RunLog.Load(file)!.Entries);
    }

    [Fact]
    public void RequiresAdmin_DefaultsToFalse_AndCacheTaskOverrides()
    {
        ICleanupTask plain = new StubTask("x");
        Assert.False(((ICleanupTask)new WslShutdownTask()).RequiresAdmin);
        Assert.False(plain.RequiresAdmin);

        var admin = Catalog.All().First(t => t.Title == "Windows Update önbelleği");
        Assert.True(admin.RequiresAdmin);
    }
}
