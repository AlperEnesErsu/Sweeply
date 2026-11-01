using Sweeply.Services;

namespace Sweeply.Tests;

public class ScheduleServiceTests
{
    [Fact]
    public void BuildCreateArguments_QuotesPathAndPassesAutoAndScheduledFlags()
    {
        var args = ScheduleService.BuildCreateArguments(@"C:\Program Files\Sweeply\Sweeply.exe", DayOfWeek.Sunday, new TimeOnly(10, 0));

        Assert.Contains(@"/TR ""\""C:\Program Files\Sweeply\Sweeply.exe\"" --auto --scheduled""", args);
        Assert.Contains("/SC WEEKLY /D SUN /ST 10:00", args);
        Assert.Contains("/RL LIMITED", args);
        Assert.Contains("/F", args);
    }

    [Theory]
    [InlineData(DayOfWeek.Monday, "MON")]
    [InlineData(DayOfWeek.Tuesday, "TUE")]
    [InlineData(DayOfWeek.Wednesday, "WED")]
    [InlineData(DayOfWeek.Thursday, "THU")]
    [InlineData(DayOfWeek.Friday, "FRI")]
    [InlineData(DayOfWeek.Saturday, "SAT")]
    [InlineData(DayOfWeek.Sunday, "SUN")]
    public void BuildCreateArguments_UsesEnglishDayCodes(DayOfWeek day, string code) =>
        Assert.Contains($"/D {code} /ST 18:00", ScheduleService.BuildCreateArguments(@"C:\x\Sweeply.exe", day, new TimeOnly(18, 0)));

    [Fact]
    public void BuildCreateArguments_PadsHour()
    {
        var args = ScheduleService.BuildCreateArguments(@"C:\x\Sweeply.exe", DayOfWeek.Friday, new TimeOnly(8, 0));
        Assert.Contains("/ST 08:00", args);
    }

    [Fact]
    public void Week_StartsMondayAndHasEveryDayOnce()
    {
        Assert.Equal(DayOfWeek.Monday, ScheduleService.Week[0]);
        Assert.Equal(7, ScheduleService.Week.Distinct().Count());
    }

    [Fact]
    public void Times_AreValidAndIncludeDefault()
    {
        Assert.All(ScheduleService.Times, t => Assert.NotNull(ScheduleService.ParseTime(t)));
        Assert.Contains(ScheduleService.DefaultTime, ScheduleService.Times);
    }

    [Theory]
    [InlineData("10:00", 10, 0)]
    [InlineData("08:30", 8, 30)]
    public void ParseTime_ReadsHourAndMinute(string text, int hour, int minute) =>
        Assert.Equal(new TimeOnly(hour, minute), ScheduleService.ParseTime(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("25:00")]
    [InlineData("10.00")]
    [InlineData("10:00; calc")]
    public void ParseTime_RejectsInvalid(string? text) => Assert.Null(ScheduleService.ParseTime(text));

    [Fact]
    public void Describe_HasNoSuffixAndUsesTurkishDayName() =>
        Assert.Equal("her çarşamba, saat 14:00", ScheduleService.Describe(DayOfWeek.Wednesday, new TimeOnly(14, 0)));

    [Theory]
    [InlineData(@"C:\dev\Sweeply\bin\Debug\net8.0-windows\Sweeply.dll")]
    [InlineData(@"C:\Program Files\dotnet\dotnet.exe")]
    [InlineData(@"C:\Users\x\Sweeply-copy.exe")]
    public void CheckExecutable_RejectsAnythingButSweeplyExe(string exe) =>
        Assert.NotNull(ScheduleService.CheckExecutable(exe));

    [Fact]
    public void CheckExecutable_AcceptsInstalledExe() =>
        Assert.Null(ScheduleService.CheckExecutable(@"C:\Users\x\AppData\Local\Programs\Sweeply\Sweeply.exe"));

    [Fact]
    public void CheckExecutable_RejectsEmpty() =>
        Assert.NotNull(ScheduleService.CheckExecutable(null));
}
