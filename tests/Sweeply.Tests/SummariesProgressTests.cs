using Sweeply.Services;
using Sweeply.Tasks;

namespace Sweeply.Tests;

public class SummariesProgressTests
{
    [Fact]
    public void Running_ShowsOneBasedPosition() =>
        Assert.Equal("Geçici dosyalar (2/5)", Summaries.Running("Geçici dosyalar", 1, 5));

    [Fact]
    public void Progress_WithFileCount_ShowsFilesAndBytes() =>
        Assert.Equal($"Tarayıcı · {1200:N0} dosya · " + Format.Bytes(5_000_000),
            Summaries.Progress("Tarayıcı", new CleanProgress(1200, 5_000_000)));

    [Fact]
    public void Progress_WithoutFileCount_OmitsFiles() =>
        Assert.Equal("Docker · " + Format.Bytes(4096),
            Summaries.Progress("Docker", new CleanProgress(0, 4096)));
}
