using System.Diagnostics;
using Sweeply.Services;

namespace Sweeply.Tests;

public class FileCleanerTests
{
    static readonly TimeSpan Day = TimeSpan.FromDays(1);

    [Fact]
    public void Measure_CountsOnlyFilesOlderThanMinAge()
    {
        using var dir = new TempDir();
        dir.File("old.tmp", 1000, TimeSpan.FromDays(3));
        dir.File("sub/old2.tmp", 500, TimeSpan.FromDays(2));
        dir.File("new.tmp", 7000);

        Assert.Equal(1500, FileCleaner.Measure(dir.Path, Day));
        Assert.Equal(8500, FileCleaner.Measure(dir.Path));
    }

    [Fact]
    public void Measure_IncludesHiddenFiles()
    {
        using var dir = new TempDir();
        var hidden = dir.File("hidden.tmp", 300, TimeSpan.FromDays(2));
        File.SetAttributes(hidden, FileAttributes.Hidden);

        Assert.Equal(300, FileCleaner.Measure(dir.Path));
    }

    [Fact]
    public void Clean_DeletesOldFiles_KeepsNewFilesAndTheRootFolder()
    {
        using var dir = new TempDir();
        var old = dir.File("old.tmp", 1000, TimeSpan.FromDays(3));
        var fresh = dir.File("fresh.tmp", 200);

        var stats = FileCleaner.Clean(dir.Path, Day);

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(fresh));
        Assert.True(Directory.Exists(dir.Path));
        Assert.Equal(1000, stats.Freed);
        Assert.Equal(1, stats.Deleted);
        Assert.Equal(1, stats.KeptNew);
        Assert.Equal(0, stats.Locked);
    }

    [Fact]
    public void Clean_RemovesFoldersThatBecameEmpty_KeepsFoldersWithRemainingFiles()
    {
        using var dir = new TempDir();
        dir.File("a/b/c/old.tmp", 10, TimeSpan.FromDays(5));
        var keep = dir.File("keep/new.tmp", 10);
        dir.Dir("already-empty");

        FileCleaner.Clean(dir.Path, Day);

        Assert.False(Directory.Exists(Path.Combine(dir.Path, "a")));
        Assert.False(Directory.Exists(Path.Combine(dir.Path, "already-empty")));
        Assert.True(File.Exists(keep));
    }

    [Fact]
    public void Clean_DeletesReadOnlyFiles()
    {
        using var dir = new TempDir();
        var file = dir.File("readonly.tmp", 64, TimeSpan.FromDays(2));
        File.SetAttributes(file, FileAttributes.ReadOnly);

        var stats = FileCleaner.Clean(dir.Path);

        Assert.False(File.Exists(file));
        Assert.Equal(64, stats.Freed);
    }

    [Fact]
    public void Clean_SkipsFilesInUse()
    {
        using var dir = new TempDir();
        var locked = dir.File("locked.tmp", 128, TimeSpan.FromDays(2));
        var free = dir.File("free.tmp", 256, TimeSpan.FromDays(2));

        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var stats = FileCleaner.Clean(dir.Path);

            Assert.True(File.Exists(locked));
            Assert.False(File.Exists(free));
            Assert.Equal(256, stats.Freed);
            Assert.Equal(1, stats.Locked);
            Assert.Equal(128, stats.LockedBytes);
            Assert.Equal("256 B silindi · 128 B kullanımda olduğu için silinemedi", FileCleaner.Describe(stats));
        }
    }

    [Fact]
    public void Describe_DoesNotMentionNewFiles_AndSaysWhenNothingWasDeleted()
    {
        Assert.Equal("Silinecek dosya yoktu", FileCleaner.Describe(new CleanStats { KeptNew = 2924 }));
        Assert.Equal("5 KB silindi", FileCleaner.Describe(new CleanStats { Freed = 5000, Deleted = 3, KeptNew = 10 }));
    }

    [Fact]
    public void Clean_DoesNotFollowJunctionsOutsideTheFolder()
    {
        using var target = new TempDir();
        using var outside = new TempDir();
        var precious = outside.File("important.docx", 999, TimeSpan.FromDays(30));

        var junction = Path.Combine(target.Path, "link");
        using (var mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junction}\" \"{outside.Path}\"")
               { CreateNoWindow = true, UseShellExecute = false })!)
        {
            mklink.WaitForExit();
        }
        Assert.True(Directory.Exists(junction), "Test için junction oluşturulamadı");

        Assert.Equal(0, FileCleaner.Measure(target.Path));
        FileCleaner.Clean(target.Path);

        Assert.True(File.Exists(precious));
        Directory.Delete(junction); // yalnızca bağlantıyı kaldırır
    }

    [Fact]
    public void MissingFolder_IsHarmless()
    {
        var missing = Path.Combine(Path.GetTempPath(), "sweeply-tests", "yok-" + Guid.NewGuid());

        Assert.Equal(0, FileCleaner.Measure(missing));
        Assert.Equal(0, FileCleaner.Clean(missing).Freed);
    }
}
