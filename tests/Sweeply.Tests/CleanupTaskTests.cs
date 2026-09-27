using Sweeply.Tasks;

namespace Sweeply.Tests;

public class TempFilesTaskTests
{
    [Fact]
    public async Task AnalyzeAndRun_OnlyTouchFilesOlderThan24Hours_AcrossAllFolders()
    {
        using var userTemp = new TempDir();
        using var winTemp = new TempDir();
        userTemp.File("setup/old.msi", 4000, TimeSpan.FromDays(10));
        var running = userTemp.File("installer-in-progress.tmp", 900, TimeSpan.FromHours(2));
        winTemp.File("old.log", 1000, TimeSpan.FromHours(30));

        var task = new TempFilesTask(userTemp.Path, winTemp.Path);

        var analysis = await task.AnalyzeAsync(CancellationToken.None);
        Assert.True(analysis.Available);
        Assert.Equal(5000, analysis.Bytes);

        var result = await task.RunAsync(CancellationToken.None);
        Assert.Equal(5000, result.FreedBytes);
        Assert.Equal("5 KB silindi", result.Message);
        Assert.True(File.Exists(running));
        Assert.Equal(0, (await task.AnalyzeAsync(CancellationToken.None)).Bytes);
    }
}

public class CrashDumpsTaskTests
{
    [Fact]
    public async Task Run_DeletesDumpsRegardlessOfAge()
    {
        using var dumps = new TempDir();
        dumps.File("app.exe.1234.dmp", 2048);
        dumps.File("old/app.exe.99.dmp", 1024, TimeSpan.FromDays(40));

        var task = new CrashDumpsTask(dumps.Path);

        Assert.Equal(3072, (await task.AnalyzeAsync(CancellationToken.None)).Bytes);
        Assert.Equal(3072, (await task.RunAsync(CancellationToken.None)).FreedBytes);
        Assert.Empty(Directory.EnumerateFileSystemEntries(dumps.Path));
    }
}

public class BrowserCacheTaskTests
{
    [Fact]
    public void CachePaths_IncludeOnlyUserProfiles()
    {
        using var userData = new TempDir();
        userData.Dir("Default");
        userData.Dir("Profile 1");
        userData.Dir("Profile 12");
        userData.Dir("System Profile");
        userData.Dir("Guest Profile");
        userData.Dir("Crashpad");

        var profiles = BrowserCacheTask.CachePaths(userData.Path)
            .Select(p => Path.GetFileName(Path.GetDirectoryName(p)))
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        Assert.Equal(["Default", "Profile 1", "Profile 12"], profiles);
    }

    [Fact]
    public async Task Run_ClearsOnlyCacheFolders_PreservesHistoryPasswordsAndCookies()
    {
        using var userData = new TempDir();
        userData.File("Default/Cache/Cache_Data/f_000001", 5000);
        userData.File("Default/Code Cache/js/index", 1000);
        userData.File("Default/GPUCache/data_0", 500);
        var history = userData.File("Default/History", 300);
        var logins = userData.File("Default/Login Data", 300);
        var cookies = userData.File("Default/Network/Cookies", 300);

        var task = new BrowserCacheTask([new("Test", "test-browser", userData.Path)], _ => false);

        Assert.Equal(6500, (await task.AnalyzeAsync(CancellationToken.None)).Bytes);
        Assert.Equal(6500, (await task.RunAsync(CancellationToken.None)).FreedBytes);
        Assert.True(File.Exists(history));
        Assert.True(File.Exists(logins));
        Assert.True(File.Exists(cookies));
    }

    [Fact]
    public async Task OpenBrowsers_AreSkipped_AndReported()
    {
        using var open = new TempDir();
        using var closed = new TempDir();
        var openCache = open.File("Default/Cache/data", 1000);
        closed.File("Default/Cache/data", 2000);

        var task = new BrowserCacheTask(
            [new("Chrome", "chrome", open.Path), new("Edge", "msedge", closed.Path)],
            process => process == "chrome");

        var analysis = await task.AnalyzeAsync(CancellationToken.None);
        Assert.True(analysis.Available);
        Assert.Equal(2000, analysis.Bytes);
        Assert.Contains("Chrome açık", analysis.Note);

        var result = await task.RunAsync(CancellationToken.None);
        Assert.Equal(2000, result.FreedBytes);
        Assert.True(File.Exists(openCache));
        Assert.Contains("Chrome açık olduğu için atlandı", result.Message);
    }

    [Fact]
    public async Task Unavailable_WhenAllBrowsersAreOpenOrMissing()
    {
        using var open = new TempDir();
        var missing = Path.Combine(open.Path, "yok");

        var allOpen = new BrowserCacheTask([new("Chrome", "chrome", open.Path)], _ => true);
        var none = new BrowserCacheTask([new("Chrome", "chrome", missing)], _ => false);

        Assert.False((await allOpen.AnalyzeAsync(CancellationToken.None)).Available);
        Assert.False((await none.AnalyzeAsync(CancellationToken.None)).Available);
    }
}

public class DockerParsingTests
{
    [Fact]
    public void ParseReclaimableBuildCache_ReadsTheBuildCacheLine()
    {
        const string output = "Images|13.22GB (68%)\r\nContainers|5.059MB (99%)\r\nLocal Volumes|532.9MB (41%)\r\nBuild Cache|19.68GB\r\n";
        Assert.Equal(19_680_000_000, DockerBuildCacheTask.ParseReclaimableBuildCache(output));
    }

    [Fact]
    public void ParseReclaimableBuildCache_ReturnsMinusOneWhenMissing() =>
        Assert.Equal(-1, DockerBuildCacheTask.ParseReclaimableBuildCache("Images|1GB\n"));

    [Fact]
    public void ParsePruneTotal_ReadsTheTotalLine()
    {
        const string output = "ID\tRECLAIMABLE\tSIZE\njchbhg3g5fnw\ttrue\t12.78MB\nTotal:\t39.86GB\n";
        Assert.Equal(39_860_000_000, DockerBuildCacheTask.ParsePruneTotal(output));
    }

    [Fact]
    public void ParsePruneTotal_ReturnsZeroWhenNothingWasPruned() =>
        Assert.Equal(0, DockerBuildCacheTask.ParsePruneTotal(""));
}
