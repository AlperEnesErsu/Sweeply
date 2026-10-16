using Sweeply.Services;
using Sweeply.Tasks;

namespace Sweeply.Tests;

public class SummariesTests
{
    const long MB = 1024 * 1024;

    [Theory]
    [InlineData(true, -1, "?")]
    [InlineData(false, -1, "?")]
    [InlineData(false, 0, "—")]
    [InlineData(true, 0, "0 B")]
    [InlineData(false, 10 * MB, "10 MB")]   // yönetici izni bekleyen görevde de boyut görünür
    public void Estimate_DistinguishesUnknownUnavailableAndSized(bool available, long bytes, string expected) =>
        Assert.Equal(expected, Summaries.Estimate(new Analysis(available, bytes)));

    [Fact]
    public void SelectionSub_AsksForASelectionWhenNothingIsSelected()
    {
        Assert.Equal("Temizlemek için en az bir görev seçin.", Summaries.SelectionSub(0));
        Assert.StartsWith("3 görev seçili", Summaries.SelectionSub(3));
    }

    [Fact]
    public void SweepResult_Cancelled_IsGoodOnlyWhenSomethingWasFreed()
    {
        var none = Summaries.SweepResult(0, 0, cancelled: true);
        Assert.Equal("Durduruldu", none.Caption);
        Assert.Equal(Tone.Normal, none.Tone);

        var some = Summaries.SweepResult(5 * MB, 0, cancelled: true);
        Assert.Equal("5 MB", some.Value);
        Assert.Equal(Tone.Good, some.Tone);
    }

    [Fact]
    public void SweepResult_NothingFreed_SaysAlreadyClean()
    {
        var h = Summaries.SweepResult(0, 0, cancelled: false);
        Assert.Equal(("Sonuç", "Zaten temiz", Tone.Normal), (h.Caption, h.Value, h.Tone));
    }

    [Fact]
    public void SweepResult_MentionsRamOnlyWhenRamWasFreed()
    {
        var diskOnly = Summaries.SweepResult(5 * MB, 0, cancelled: false);
        Assert.Equal(("Temizlendi", Tone.Good), (diskOnly.Caption, diskOnly.Tone));
        Assert.DoesNotContain("RAM", diskOnly.Sub);

        var withRam = Summaries.SweepResult(5 * MB, 200 * MB, cancelled: false);
        Assert.Contains("200 MB RAM", withRam.Sub);
    }

    [Fact]
    public void PreviewRows_TotalsOnlyDiskTasksAndCollapsesTheTail()
    {
        var many = Enumerable.Range(0, Summaries.PreviewPerTask + 3)
            .Select(i => new PreviewEntry($@"C:\tmp\f{i}", MB, 2)).ToList();
        var (rows, total) = Summaries.PreviewRows(new[]
        {
            ("Disk görevi", TaskKind.Disk, new Preview(many)),
            ("RAM görevi", TaskKind.Ram, new Preview([new PreviewEntry("ram", 50 * MB, 0, IsCommand: true)])),
            ("Boş görev", TaskKind.Disk, new Preview([], "not")),
        });

        Assert.Equal((Summaries.PreviewPerTask + 3) * MB, total);   // RAM görevi toplamı etkilemez

        // başlık + 12 satır + "ve 3 öğe daha"
        Assert.True(rows[0].IsHeader);
        Assert.Equal("ve 3 öğe daha", rows[Summaries.PreviewPerTask + 1].Primary);
        Assert.Equal("6 dosya", rows[Summaries.PreviewPerTask + 1].Secondary);

        var ramHeader = rows.FindIndex(r => r.Primary == "RAM görevi");
        Assert.Equal("Komut: ram", rows[ramHeader + 1].Primary);

        var empty = rows.FindIndex(r => r.Primary == "Boş görev");
        Assert.Equal("not", rows[empty].Secondary);
        Assert.Equal("Silinecek bir şey yok", rows[empty + 1].Primary);
    }

    [Fact]
    public void BootSummary_CountsFastStartupsSinceTheLastFullBoot()
    {
        var now = new DateTime(2026, 10, 15, 9, 0, 0);
        var boots = new[]
        {
            new BootEvent(now, BootKind.FastStartup),
            new BootEvent(now.AddDays(-1), BootKind.FastStartup),
            new BootEvent(now.AddDays(-2), BootKind.Full),
            new BootEvent(now.AddDays(-3), BootKind.FastStartup),
        };

        var (title, message) = Summaries.BootSummary(boots, fastStartup: true);
        Assert.Equal("Hızlı Başlangıç açık", title);
        Assert.Contains("Son tam açılış:", message);
        Assert.Contains("O zamandan beri 2 kez", message);
    }

    [Fact]
    public void BootSummary_WithoutFastStartup_HasNoCount()
    {
        var (title, message) = Summaries.BootSummary([], fastStartup: false);
        Assert.Equal("Açılış geçmişi", title);
        Assert.DoesNotContain("Son tam açılış", message);
        Assert.StartsWith("Hızlı Başlangıç kapalı", message);
    }
}
