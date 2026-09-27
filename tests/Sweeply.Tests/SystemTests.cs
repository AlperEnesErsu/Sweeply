using System.Diagnostics;
using Sweeply.Services;

namespace Sweeply.Tests;

public class ProcessServiceTests
{
    [Fact]
    public void Group_SumsByNameIgnoringCase_AndSortsByMemory()
    {
        var groups = ProcessService.Group(
        [
            ("chrome", 300), ("Chrome", 200), ("code", 700), ("notepad", 50),
        ], count: 10);

        Assert.Equal(["code", "chrome", "notepad"], groups.Select(g => g.Name.ToLowerInvariant()));
        Assert.Equal(500, groups[1].Bytes);
        Assert.Equal(2, groups[1].Count);
    }

    [Fact]
    public void Group_HidesSystemProcesses()
    {
        var groups = ProcessService.Group([("svchost", 9000), ("explorer", 8000), ("vmmemWSL", 7000), ("Sweeply", 6000), ("app", 10)], 10);

        Assert.Equal(["app"], groups.Select(g => g.Name));
    }

    [Fact]
    public void Group_TakesTopN_AndSharesAreRelativeToTheLargest()
    {
        var groups = ProcessService.Group([("a", 1000), ("b", 500), ("c", 250), ("d", 100)], count: 3);

        Assert.Equal(3, groups.Count);
        Assert.Equal([1.0, 0.5, 0.25], groups.Select(g => g.Share));
    }

    [Fact]
    public void Group_EmptyInput_ReturnsEmpty() =>
        Assert.Empty(ProcessService.Group([], 5));

    [Fact]
    public void GetTop_ReturnsRealProcesses()
    {
        var top = ProcessService.GetTop(5);

        Assert.InRange(top.Count, 1, 5);
        Assert.All(top, g => Assert.True(g.Bytes > 0));
        Assert.Equal(top.OrderByDescending(g => g.Bytes).Select(g => g.Name), top.Select(g => g.Name));
    }
}

public class SystemInfoTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData(new byte[0], false)]
    [InlineData(new byte[] { 2, 0, 0 }, false)]
    [InlineData(new byte[] { 6, 0, 0 }, false)]
    [InlineData(new byte[] { 3, 0, 0 }, true)]
    [InlineData(new byte[] { 7, 0, 0 }, true)]
    public void StartupApprovedFlag_OddFirstByteMeansDisabled(byte[]? data, bool disabled) =>
        Assert.Equal(disabled, SystemInfo.IsDisabledFlag(data));

    [Fact]
    public void Snapshot_ReturnsPlausibleValues()
    {
        var s = SystemInfo.GetSnapshot();

        Assert.True(s.RamTotal > 0);
        Assert.InRange(s.RamAvailable, 0, s.RamTotal);
        Assert.InRange(s.RamLoad, 0, 100);
        Assert.True(s.DiskTotal > 0);
        Assert.InRange(s.DiskFree, 0, s.DiskTotal);
        Assert.InRange(s.DiskUsedPercent, 0, 100);
        Assert.True(s.Uptime > TimeSpan.Zero);
        Assert.True(s.StartupCount >= 0);
    }
}

public class HealthTipsTests
{
    static HealthSnapshot Snapshot(int ramLoad = 40, double diskUsed = 50, double uptimeDays = 1, int startup = 3) =>
        new(16L << 30, (long)((16L << 30) * (1 - ramLoad / 100.0)), ramLoad, "C:",
            1000, (long)(1000 * (1 - diskUsed / 100)), TimeSpan.FromDays(uptimeDays), startup);

    static string?[] All(HealthSnapshot s) =>
        [HealthTips.Ram(s), HealthTips.Disk(s), HealthTips.Uptime(s), HealthTips.Startup(s)];

    [Fact]
    public void HealthySystem_HasNoWarnings() => Assert.All(All(Snapshot()), Assert.Null);

    [Fact]
    public void EachProblem_WarnsOnlyUnderItsOwnMetric()
    {
        Assert.Equal("%85 dolu; SSD'ler dolunca yavaşlar", HealthTips.Disk(Snapshot(diskUsed: 85)));
        Assert.Single(All(Snapshot(diskUsed: 85)), w => w != null);
        Assert.NotNull(HealthTips.Ram(Snapshot(ramLoad: 90)));
        Assert.NotNull(HealthTips.Uptime(Snapshot(uptimeDays: 3.2)));
        Assert.NotNull(HealthTips.Startup(Snapshot(startup: 8)));
        Assert.All(All(Snapshot(90, 95, 5, 12)), Assert.NotNull);
    }

    [Fact]
    public void JustBelowThresholds_HasNoWarnings() =>
        Assert.All(All(Snapshot(ramLoad: 84, diskUsed: 84.4, uptimeDays: 2.9, startup: 7)), Assert.Null);
}

public class CommandRunnerTests
{
    [Fact]
    public async Task Run_ReturnsExitCodeAndOutput()
    {
        var (code, output) = await CommandRunner.RunAsync("cmd.exe", "/c echo merhaba & exit 3", TimeSpan.FromSeconds(10));

        Assert.Equal(3, code);
        Assert.Contains("merhaba", output);
    }

    [Fact]
    public async Task Run_KillsTheProcessOnTimeout()
    {
        var clock = Stopwatch.StartNew();
        var (code, output) = await CommandRunner.RunAsync("cmd.exe", "/c ping -n 30 127.0.0.1 >nul", TimeSpan.FromSeconds(1));

        Assert.Equal(-1, code);
        Assert.Equal("zaman aşımı", output);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"Süre: {clock.Elapsed}");
    }

    [Fact]
    public async Task Run_MissingExecutable_DoesNotThrow()
    {
        var (code, _) = await CommandRunner.RunAsync("olmayan-program-12345.exe", "", TimeSpan.FromSeconds(5));
        Assert.Equal(-1, code);
    }

    [Fact]
    public void FindOnPath_FindsSystemTools_AndReturnsNullForUnknown()
    {
        Assert.NotNull(CommandRunner.FindOnPath("cmd.exe"));
        Assert.Null(CommandRunner.FindOnPath("olmayan-program-12345.exe"));
    }
}
