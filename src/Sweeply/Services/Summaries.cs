using Sweeply.Controls;
using Sweeply.Models;
using Sweeply.Tasks;

namespace Sweeply.Services;

public enum Tone { Normal, Good }

/// <summary>Başlık kartının içeriği: üst etiket, büyük değer, alt satır ve değerin rengi.</summary>
public sealed record Headline(string Caption, string Value, string Sub, Tone Tone = Tone.Normal);

/// <summary>Ana pencerenin metin kararları. WPF'e dokunmaz, böylece test edilebilir.</summary>
public static class Summaries
{
    /// <summary>Önizlemede görev başına gösterilen satır; kalanı tek bir "ve N öğe daha" satırında toplanır.</summary>
    public const int PreviewPerTask = 12;

    /// <summary>
    /// Görev satırındaki tahmin. Yönetici izni bekleyen görevlerde de boyut gösterilir:
    /// kullanıcı neyi kaçırdığını görsün.
    /// </summary>
    public static string Estimate(Analysis analysis) =>
        analysis.Bytes < 0 ? "?"
        : !analysis.Available && analysis.Bytes == 0 ? "—"
        : Format.Bytes(analysis.Bytes);

    /// <summary>Çalışan görev satırı: "Geçici dosyalar (2/5)".</summary>
    public static string Running(string title, int index, int count) => $"{title} ({index + 1}/{count})";

    /// <summary>Görev içi ilerleme: dosya sayısı yalnızca biliniyorsa gösterilir.</summary>
    public static string Progress(string title, CleanProgress p) =>
        p.Files > 0
            ? $"{title} · {p.Files:N0} dosya · {Format.Bytes(p.Bytes)}"
            : $"{title} · {Format.Bytes(p.Bytes)}";

    public static string SelectionSub(int selectedCount) =>
        selectedCount == 0
            ? "Temizlemek için en az bir görev seçin."
            : $"{selectedCount} görev seçili · Temp klasöründe sadece 24 saatten eski dosyalar silinir.";

    public static Headline SweepResult(long disk, long ram, bool cancelled)
    {
        if (cancelled)
            return new Headline("Durduruldu", Format.Bytes(disk),
                "O ana kadar silinenler silindi; kalan görevler çalıştırılmadı.",
                disk > 0 ? Tone.Good : Tone.Normal);

        if (disk == 0 && ram == 0)
            return new Headline("Sonuç", "Zaten temiz",
                "Silinebilecek bir şey bulunamadı. Ayrıntılar her görevin altında.");

        return new Headline("Temizlendi", Format.Bytes(disk),
            ram > 0
                ? $"disk alanı açıldı · ayrıca {Format.Bytes(ram)} RAM boşaldı"
                : "disk alanı açıldı · ayrıntılar her görevin altında",
            Tone.Good);
    }

    /// <summary>Önizleme penceresinin satırları ve yalnızca disk görevlerinden gelen toplam.</summary>
    public static (List<ListRow> Rows, long DiskTotal) PreviewRows(
        IEnumerable<(string Title, TaskKind Kind, Preview Preview)> previews)
    {
        var rows = new List<ListRow>();
        long total = 0;
        foreach (var (title, kind, preview) in previews)
        {
            long sum = preview.Entries.Sum(x => x.Bytes);
            if (kind == TaskKind.Disk) total += sum;
            rows.Add(new ListRow(title, preview.Note, Format.Bytes(sum), IsHeader: true));

            if (preview.Entries.Count == 0) rows.Add(new ListRow("Silinecek bir şey yok"));
            foreach (var entry in preview.Entries.Take(PreviewPerTask))
            {
                rows.Add(entry.IsCommand
                    ? new ListRow("Komut: " + entry.Label, "Aracın kendi temizleme komutu çalıştırılır", Format.Bytes(entry.Bytes))
                    : new ListRow(Format.ShortPath(entry.Label), $"{entry.Files:N0} dosya", Format.Bytes(entry.Bytes)));
            }
            var rest = preview.Entries.Skip(PreviewPerTask).ToList();
            if (rest.Count > 0)
                rows.Add(new ListRow($"ve {rest.Count:N0} öğe daha", $"{rest.Sum(x => x.Files):N0} dosya", Format.Bytes(rest.Sum(x => x.Bytes))));
        }
        return (rows, total);
    }

    /// <summary>
    /// Zamanlanmış temizliğin kaydı: seçili her görev, çalıştıysa sonucuyla, çalışmadıysa nedeniyle
    /// (ör. yönetici izni gerektiği için) yer alır.
    /// </summary>
    public static RunLog ScheduledRunLog(IEnumerable<CleanupItem> items, long disk, long ram, bool cancelled, DateTime time) => new()
    {
        Time = time,
        DiskFreed = disk,
        RamFreed = ram,
        Cancelled = cancelled,
        Entries = items.Where(i => i.IsEnabled).Select(i => i.State switch
        {
            ItemState.Done => new RunLogEntry(i.Title, RunOutcome.Done, i.Status),
            ItemState.Failed => new RunLogEntry(i.Title, RunOutcome.Failed, i.Status),
            ItemState.Skipped => new RunLogEntry(i.Title, RunOutcome.Skipped, i.Status),
            _ => new RunLogEntry(i.Title, RunOutcome.NotRun, i.Note ?? "Şu an çalıştırılamıyordu"),
        }).ToList(),
    };

    static string OutcomeText(RunOutcome outcome) => outcome switch
    {
        RunOutcome.Done => "Tamamlandı",
        RunOutcome.Failed => "Başarısız",
        RunOutcome.Skipped => "Durduruldu",
        _ => "Çalışmadı",
    };

    /// <summary>Son zamanlanmış temizliği gösteren pencerenin başlığı, açıklaması ve satırları.</summary>
    public static (string Title, string Message, List<ListRow> Rows) RunLogSummary(RunLog log)
    {
        var parts = new List<string> { log.Time.ToString("dd MMMM dddd, HH:mm") };
        parts.Add(log.DiskFreed > 0 ? $"{Format.Bytes(log.DiskFreed)} disk alanı açıldı" : "disk alanı açılmadı");
        if (log.RamFreed > 0) parts.Add($"{Format.Bytes(log.RamFreed)} RAM boşaldı");
        if (log.Cancelled) parts.Add("temizlik yarıda durduruldu");

        int problems = log.Entries.Count(e => e.Outcome != RunOutcome.Done);
        var rows = log.Entries.Select(e => new ListRow(e.Title, e.Message, OutcomeText(e.Outcome))).ToList();
        if (rows.Count == 0) rows.Add(new ListRow("Seçili görev yoktu"));

        return (problems > 0 ? $"Son otomatik temizlik · {problems} görev tamamlanmadı" : "Son otomatik temizlik",
            string.Join(" · ", parts), rows);
    }

    /// <summary>Açılış geçmişi penceresinin başlığı ve açıklaması (en yeni açılış başta).</summary>
    public static (string Title, string Message) BootSummary(IReadOnlyList<BootEvent> boots, bool fastStartup)
    {
        var lastFull = boots.FirstOrDefault(b => b.Kind == BootKind.Full);
        string since = lastFull != null ? $"Son tam açılış: {lastFull.Time:dd MMMM HH:mm}. " : "";

        if (!fastStartup)
            return ("Açılış geçmişi", since + "Hızlı Başlangıç kapalı; her \"Kapat\" tam bir kapatmadır.");

        int fastSinceFull = boots.TakeWhile(b => b.Kind != BootKind.Full).Count(b => b.Kind == BootKind.FastStartup);
        return ("Hızlı Başlangıç açık",
            since + (fastSinceFull > 0 ? $"O zamandan beri {fastSinceFull} kez kapatıp açtınız ama hiçbiri tam kapatma değildi. " : "") +
            "Hızlı Başlangıç açıkken \"Kapat\" Windows çekirdeğini diske kaydeder; bellek ve sürücüler sıfırlanmaz.");
    }
}
