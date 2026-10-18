using Sweeply.Services;

namespace Sweeply.Tests;

public class ScheduleServiceTests
{
    [Fact]
    public void BuildCreateArguments_QuotesPathAndPassesAutoFlag()
    {
        var args = ScheduleService.BuildCreateArguments(@"C:\Program Files\Sweeply\Sweeply.exe");

        Assert.Contains(@"/TR ""\""C:\Program Files\Sweeply\Sweeply.exe\"" --auto""", args);
        Assert.Contains("/SC WEEKLY /D SUN /ST 10:00", args);
        Assert.Contains("/RL LIMITED", args);
    }

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
