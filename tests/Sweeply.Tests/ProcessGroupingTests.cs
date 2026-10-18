using Sweeply.Services;

namespace Sweeply.Tests;

public class ProcessGroupingTests
{
    [Fact]
    public void Group_HidesUserProtectedProcesses_OnTopOfDefaults()
    {
        var groups = ProcessService.Group(
            [("obs64", 900), ("svchost", 800), ("app", 10)],
            count: 10,
            extraProtected: ["OBS64"]);

        Assert.Equal(["app"], groups.Select(g => g.Name));
    }

    [Fact]
    public void Group_WithoutExtraList_BehavesAsBefore()
    {
        var groups = ProcessService.Group([("obs64", 900), ("app", 10)], count: 10);

        Assert.Equal(["obs64", "app"], groups.Select(g => g.Name));
    }
}
