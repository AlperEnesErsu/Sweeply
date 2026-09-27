using Sweeply.Services;
using Sweeply.Tasks;

namespace Sweeply.Tests;

/// <summary>Gerçek kullanıcı klasörleri yerine geçici bir klasör ağacında çalışan katalog.</summary>
public sealed class FakeProfile : IDisposable
{
    readonly TempDir _dir = new();

    public Catalog.Roots Roots { get; }

    public FakeProfile()
    {
        Roots = new Catalog.Roots(
            _dir.Dir("Local"), _dir.Dir("Roaming"), _dir.Dir("Home"), _dir.Dir("Temp"), _dir.Dir("Windows"));
    }

    public string File(string relative, int bytes, TimeSpan age = default) => _dir.File(relative, bytes, age);

    public void Dispose() => _dir.Dispose();
}

/// <summary>Bildirimleri senkron toplayan IProgress (Progress&lt;T&gt; gibi başka bir iş parçacığına göndermez).</summary>
sealed class ListProgress : IProgress<CleanProgress>
{
    public readonly List<CleanProgress> Reports = new();
    public void Report(CleanProgress value) { lock (Reports) Reports.Add(value); }
}

static class TaskExtensions
{
    // Testlerde hiçbir gerçek işlem "açık" sayılmaz ve PATH'teki gerçek araçlar kullanılmaz.
    public static CacheTask Isolated(this CacheTask task) =>
        task with { IsRunning = _ => false, FindTool = _ => null, IsAdmin = () => true };

    public static Task<Analysis> Analyze(this ICleanupTask task) => task.AnalyzeAsync(CancellationToken.None);
    public static Task<TaskResult> Run(this ICleanupTask task) => task.RunAsync(null, CancellationToken.None);
}

public class SystemTaskTests
{
    [Fact]
    public async Task Temp_OnlyTouchesFilesOlderThan24Hours_InBothTempFolders()
    {
        using var p = new FakeProfile();
        p.File(@"Temp\setup\old.msi", 4000, TimeSpan.FromDays(10));
        var running = p.File(@"Temp\installer-in-progress.tmp", 900, TimeSpan.FromHours(2));
        p.File(@"Windows\Temp\old.log", 1000, TimeSpan.FromHours(30));

        var task = Catalog.Temp(p.Roots).Isolated();

        Assert.Equal(5000, (await task.Analyze()).Bytes);
        var result = await task.Run();
        Assert.Equal(5000, result.FreedBytes);
        Assert.Equal("5 KB silindi", result.Message);
        Assert.True(File.Exists(running));
    }

    [Fact]
    public async Task CrashDumps_AreDeletedRegardlessOfAge()
    {
        using var p = new FakeProfile();
        p.File(@"Local\CrashDumps\app.exe.1234.dmp", 2048);
        p.File(@"Local\Microsoft\Windows\WER\ReportArchive\r1\Report.wer", 1024, TimeSpan.FromDays(40));

        var task = Catalog.CrashDumps(p.Roots).Isolated();

        Assert.Equal(3072, (await task.Analyze()).Bytes);
        Assert.Equal(3072, (await task.Run()).FreedBytes);
    }

    [Fact]
    public async Task AdminTasks_ShowSizeButDeleteNothing_WithoutAdmin()
    {
        using var p = new FakeProfile();
        var update = p.File(@"Windows\SoftwareDistribution\Download\kb123.cab", 5000);

        var task = Catalog.WindowsUpdate(p.Roots).Isolated() with { IsAdmin = () => false };

        var analysis = await task.Analyze();
        Assert.False(analysis.Available);
        Assert.Equal(5000, analysis.Bytes);
        Assert.Contains("Yönetici", analysis.Note);

        var result = await task.Run();
        Assert.Equal(0, result.FreedBytes);
        Assert.True(File.Exists(update));
    }
}

public class BrowserAndAppTaskTests
{
    [Fact]
    public void ChromiumCachePaths_IncludeOnlyUserProfiles()
    {
        using var dir = new TempDir();
        foreach (var name in new[] { "Default", "Profile 1", "Profile 12", "System Profile", "Guest Profile", "Crashpad" })
            dir.Dir(name);

        var profiles = Catalog.ChromiumCachePaths(dir.Path)
            .Select(p => Path.GetFileName(Path.GetDirectoryName(p)))
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        Assert.Equal(["Default", "Profile 1", "Profile 12"], profiles);
    }

    [Fact]
    public async Task Browsers_ClearChromeFirefoxAndOperaCaches_KeepHistoryCookiesAndPasswords()
    {
        using var p = new FakeProfile();
        p.File(@"Local\Google\Chrome\User Data\Default\Cache\Cache_Data\f_000001", 1000);
        p.File(@"Local\Google\Chrome\User Data\Default\Code Cache\js\index", 200);
        p.File(@"Local\Mozilla\Firefox\Profiles\abc.default-release\cache2\entries\E1", 2000);
        p.File(@"Local\Opera Software\Opera Stable\Cache\Cache_Data\data_1", 500);
        string[] keep =
        [
            p.File(@"Local\Google\Chrome\User Data\Default\History", 300),
            p.File(@"Local\Google\Chrome\User Data\Default\Login Data", 300),
            p.File(@"Local\Google\Chrome\User Data\Default\Network\Cookies", 300),
            p.File(@"Local\Mozilla\Firefox\Profiles\abc.default-release\safebrowsing\list", 300),
            p.File(@"Roaming\Mozilla\Firefox\Profiles\abc.default-release\places.sqlite", 300),
            p.File(@"Roaming\Opera Software\Opera Stable\Cookies", 300),
        ];

        var task = Catalog.Browsers(p.Roots).Isolated();

        Assert.Equal(3700, (await task.Analyze()).Bytes);
        Assert.Equal(3700, (await task.Run()).FreedBytes);
        Assert.All(keep, f => Assert.True(File.Exists(f), f));
    }

    [Fact]
    public async Task OpenBrowsers_AreSkipped_AndReported()
    {
        using var p = new FakeProfile();
        var chromeCache = p.File(@"Local\Google\Chrome\User Data\Default\Cache\data", 1000);
        p.File(@"Local\Microsoft\Edge\User Data\Default\Cache\data", 2000);

        var task = Catalog.Browsers(p.Roots).Isolated() with { IsRunning = name => name == "chrome" };

        var analysis = await task.Analyze();
        Assert.Equal(2000, analysis.Bytes);
        Assert.Contains("Chrome açık", analysis.Note);

        var result = await task.Run();
        Assert.Equal(2000, result.FreedBytes);
        Assert.True(File.Exists(chromeCache));
        Assert.Contains("Chrome açık olduğu için atlandı", result.Message);
    }

    [Fact]
    public async Task NothingInstalled_IsUnavailable()
    {
        using var p = new FakeProfile();
        var analysis = await Catalog.Browsers(p.Roots).Isolated().Analyze();

        Assert.False(analysis.Available);
        Assert.Equal("Bu bilgisayarda bulunamadı.", analysis.Note);
    }

    [Fact]
    public async Task AppCaches_ClearElectronCaches_KeepSettingsAndLogins()
    {
        using var p = new FakeProfile();
        p.File(@"Roaming\Code\CachedData\abc\chrome\js\1", 4000);
        p.File(@"Roaming\Code\Cache\Cache_Data\f_1", 1000);
        p.File(@"Roaming\discord\Code Cache\js\2", 500);
        string[] keep =
        [
            p.File(@"Roaming\Code\User\settings.json", 100),
            p.File(@"Roaming\Code\User\globalStorage\state.vscdb", 100),
            p.File(@"Roaming\discord\Local Storage\leveldb\000003.log", 100),
            p.File(@"Roaming\discord\Cookies", 100),
        ];

        var result = await Catalog.AppCaches(p.Roots).Isolated().Run();

        Assert.Equal(5500, result.FreedBytes);
        Assert.All(keep, f => Assert.True(File.Exists(f), f));
    }

    [Fact]
    public async Task Spotify_IsOptIn_AndWarnsAboutOfflineSongs()
    {
        using var p = new FakeProfile();
        p.File(@"Local\Spotify\Storage\a\b.file", 1000);

        var task = Catalog.Spotify(p.Roots).Isolated();

        Assert.False(task.EnabledByDefault);
        Assert.Contains("çevrimdışı", (await task.Analyze()).Note);
    }
}

public class DeveloperTaskTests
{
    [Fact]
    public async Task DevCaches_WithoutTools_CleanFolders_ButNeverTouchThePnpmStore()
    {
        using var p = new FakeProfile();
        p.File(@"Local\npm-cache\_cacache\content-v2\sha512\aa", 3000);
        p.File(@"Local\pip\cache\http\x", 1000);
        p.File(@"Local\NuGet\v3-cache\abc\pkg.nupkg", 2000);
        var npmLogs = p.File(@"Local\npm-cache\_logs\debug.log", 50);
        var pnpmStore = p.File(@"Local\pnpm\store\v3\files\00\abc", 5000);
        var nugetPackages = p.File(@"Home\.nuget\packages\newtonsoft.json\13.0.3\lib.dll", 5000);

        var task = Catalog.DevCaches(p.Roots).Isolated();

        Assert.Equal(6000, (await task.Analyze()).Bytes);
        Assert.Equal(6000, (await task.Run()).FreedBytes);
        Assert.True(File.Exists(pnpmStore), "pnpm deposu elle silinmemeli");
        Assert.True(File.Exists(nugetPackages), "~/.nuget/packages bu görevde silinmemeli");
        Assert.True(File.Exists(npmLogs));
    }

    [Fact]
    public async Task PackageStores_AreOptIn_AndKeepCargoInstalledTools()
    {
        using var p = new FakeProfile();
        p.File(@"Home\.cargo\registry\cache\index\serde-1.0.crate", 2000);
        p.File(@"Home\.gradle\caches\modules-2\files\x.jar", 3000);
        var cargoTool = p.File(@"Home\.cargo\bin\cargo-watch.exe", 7000);
        var cargoConfig = p.File(@"Home\.cargo\config.toml", 10);

        var task = Catalog.PackageStores(p.Roots).Isolated();

        Assert.False(task.EnabledByDefault);
        Assert.Equal(5000, (await task.Run()).FreedBytes);
        Assert.True(File.Exists(cargoTool));
        Assert.True(File.Exists(cargoConfig));
    }

    [Fact]
    public async Task FailingCleanCommand_IsReported()
    {
        using var dir = new TempDir();
        var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        CacheTask Make(string args) => new CacheTask
        {
            Title = "Test",
            Description = "",
            Group = TaskGroup.Developer,
            Apps = () => [new CacheApp("Araç", [], [new CommandSource("arac.exe", args, [dir.Path])])],
        }.Isolated() with { FindTool = _ => cmd };

        Assert.Equal("Silinecek dosya yoktu", (await Make("/c exit 0").Run()).Message);
        Assert.Contains("Araç temizleme komutu hata verdi", (await Make("/c exit 5").Run()).Message);
    }
}

public class ProgressCancelPreviewTests
{
    [Fact]
    public async Task Run_ReportsLiveProgress_EndingWithTheTotal()
    {
        using var p = new FakeProfile();
        for (int i = 0; i < 450; i++) p.File($@"Local\CrashDumps\d{i}.dmp", 10);
        var progress = new ListProgress();

        await Catalog.CrashDumps(p.Roots).Isolated().RunAsync(progress, CancellationToken.None);

        Assert.True(progress.Reports.Count >= 3);
        Assert.Equal(new CleanProgress(450, 4500), progress.Reports[^1]);
        Assert.Equal(progress.Reports.OrderBy(r => r.Bytes), progress.Reports);
    }

    [Fact]
    public async Task Run_WithCancelledToken_DeletesNothing_AndSaysStopped()
    {
        using var p = new FakeProfile();
        var dump = p.File(@"Local\CrashDumps\a.dmp", 100);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await Catalog.CrashDumps(p.Roots).Isolated().RunAsync(null, cts.Token);

        Assert.Equal("Durduruldu", result.Message);
        Assert.True(File.Exists(dump));
    }

    [Fact]
    public async Task Preview_GroupsByTopLevelItem_SortsBySize_AndDeletesNothing()
    {
        using var p = new FakeProfile();
        p.File(@"Temp\setup\a.cab", 3000, TimeSpan.FromDays(5));
        p.File(@"Temp\setup\b.cab", 1000, TimeSpan.FromDays(5));
        p.File(@"Temp\setup\new.tmp", 9000);
        var log = p.File(@"Temp\old.log", 500, TimeSpan.FromDays(2));

        var preview = await Catalog.Temp(p.Roots).Isolated().PreviewAsync(CancellationToken.None);

        Assert.Equal(2, preview.Entries.Count);
        Assert.Equal("setup", Path.GetFileName(preview.Entries[0].Label));
        Assert.Equal(4000, preview.Entries[0].Bytes);
        Assert.Equal(2, preview.Entries[0].Files);
        Assert.Equal(500, preview.Entries[1].Bytes);
        Assert.True(File.Exists(log));
    }

    [Fact]
    public async Task Preview_ShowsCommands_ForToolBasedCaches()
    {
        using var p = new FakeProfile();
        p.File(@"Local\npm-cache\_cacache\x", 100);

        var task = Catalog.DevCaches(p.Roots).Isolated() with { FindTool = name => name == "npm.cmd" ? "npm.cmd" : null };
        var preview = await task.PreviewAsync(CancellationToken.None);

        var npm = Assert.Single(preview.Entries, e => e.IsCommand);
        Assert.Equal("npm cache clean --force", npm.Label);
        Assert.Equal(100, npm.Bytes);
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
