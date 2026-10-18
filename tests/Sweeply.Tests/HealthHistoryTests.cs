using Sweeply.Services;

namespace Sweeply.Tests;

public class HealthHistoryTests
{
    static readonly DateOnly Today = new(2026, 10, 18);

    static DaySample Day(int daysAgo, double disk) => new(Today.AddDays(-daysAgo), disk, 50);

    [Fact]
    public void Record_SameDay_ReplacesInsteadOfAppending()
    {
        var history = new HealthHistory(Path.Combine(Path.GetTempPath(), "sweeply-tests", Guid.NewGuid().ToString("N"), "h.json"));
        var noon = new DateTime(2026, 10, 18, 12, 0, 0);

        history.Record(40, 50, noon);
        history.Record(42, 55, noon.AddMinutes(3));

        Assert.Single(history.Samples);
        Assert.Equal(42, history.Samples[0].DiskUsedPercent);
    }

    [Fact]
    public void Record_DropsSamplesOlderThanKeepWindow()
    {
        var path = Path.Combine(Path.GetTempPath(), "sweeply-tests", Guid.NewGuid().ToString("N"), "h.json");
        var history = new HealthHistory(path);
        history.Record(40, 50, new DateTime(2026, 1, 1, 9, 0, 0));
        history.Record(41, 50, new DateTime(2026, 10, 18, 9, 0, 0));

        Assert.Single(history.Samples);
        Assert.Equal(Today, history.Samples[0].Day);
    }

    [Fact]
    public void Save_ThenLoad_RestoresSamples()
    {
        var path = Path.Combine(Path.GetTempPath(), "sweeply-tests", Guid.NewGuid().ToString("N"), "h.json");
        var now = new DateTime(2026, 10, 18, 9, 0, 0);
        var history = new HealthHistory(path);
        history.Record(61.5, 70, now);
        Assert.True(history.Save(now));

        var loaded = new HealthHistory(path);

        Assert.Single(loaded.Samples);
        Assert.Equal(61.5, loaded.Samples[0].DiskUsedPercent);
    }

    [Fact]
    public void DiskTrend_RiseOverWeek_ReturnsMessage() =>
        Assert.Equal("Son 7 günde disk %8 daha doldu",
            HealthTips.DiskTrend([Day(7, 50), Day(0, 58)], Today));

    [Fact]
    public void DiskTrend_TooLittleHistory_ReturnsNull() =>
        Assert.Null(HealthTips.DiskTrend([Day(3, 50), Day(0, 70)], Today));

    [Fact]
    public void DiskTrend_SmallChange_ReturnsNull() =>
        Assert.Null(HealthTips.DiskTrend([Day(10, 50), Day(0, 53)], Today));

    [Fact]
    public void DiskTrend_OldSamplesOutsideWindow_AreIgnored() =>
        Assert.Null(HealthTips.DiskTrend([Day(30, 10), Day(20, 60), Day(0, 61)], Today));

    [Fact]
    public void DiskTrend_NoSamples_ReturnsNull() =>
        Assert.Null(HealthTips.DiskTrend([], Today));
}
