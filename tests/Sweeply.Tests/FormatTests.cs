using Sweeply.Services;

namespace Sweeply.Tests;

public class FormatTests
{
    const long KB = 1024, MB = KB * 1024, GB = MB * 1024;

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(-50, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1536, "2 KB")]
    [InlineData(10 * MB, "10 MB")]
    [InlineData(GB + GB / 2, "1,5 GB")]
    [InlineData(2 * 1024 * GB, "2,0 TB")]
    public void Bytes_FormatsWithTurkishDecimalSeparator(long bytes, string expected) =>
        Assert.Equal(expected, Format.Bytes(bytes));

    [Theory]
    [InlineData(1023 * KB + 800, "1 MB")]         // 1023,8 KB yuvarlanınca "1024 KB" değil
    [InlineData(1024 * MB - 100 * KB, "1,0 GB")]  // 1023,9 MB yuvarlanınca "1024 MB" değil
    public void Bytes_RoundingNeverShows1024OfAUnit(long bytes, string expected) =>
        Assert.Equal(expected, Format.Bytes(bytes));

    [Fact]
    public void Duration_UsesDaysAfter24Hours()
    {
        Assert.Equal("3 gün 3 sa", Format.Duration(TimeSpan.FromHours(75)));
        Assert.Equal("1 sa 30 dk", Format.Duration(TimeSpan.FromMinutes(90)));
    }

    [Theory]
    [InlineData("19.68GB", 19_680_000_000)]
    [InlineData("532.9MB (41%)", 532_900_000)]
    [InlineData("12.78kB", 12_780)]
    [InlineData("0B", 0)]
    [InlineData("  1.5 GB", 1_500_000_000)]
    [InlineData("39.86GB", 39_860_000_000)]
    [InlineData("abc", 0)]
    [InlineData("", 0)]
    public void ParseDockerSize_UsesDecimalUnits(string text, long expected) =>
        Assert.Equal(expected, Format.ParseDockerSize(text));
}
