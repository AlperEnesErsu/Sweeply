namespace Sweeply.Services;

/// <summary>Her ölçümün altında gösterilecek kısa uyarı; sorun yoksa null.</summary>
public static class HealthTips
{
    public const double DiskFullPercent = 85;
    public const int RamFullPercent = 85;
    public const int UptimeDays = 3;
    public const int StartupItems = 8;

    public static string? Ram(HealthSnapshot s) =>
        s.RamLoad >= RamFullPercent ? "Neredeyse dolu; kullanmadığınız programları kapatın" : null;

    public static string? Disk(HealthSnapshot s) =>
        s.DiskUsedPercent >= DiskFullPercent ? $"%{s.DiskUsedPercent:0} dolu; SSD'ler dolunca yavaşlar" : null;

    /// <summary>Eğilim penceresinde disk bu kadar puandan fazla dolduysa uyarılır.</summary>
    public const double TrendRisePoints = 5;

    public const int TrendWindowDays = 14;

    /// <summary>
    /// Son <see cref="TrendWindowDays"/> günde disk ne kadar doldu? En az bir haftalık geçmiş ve
    /// <see cref="TrendRisePoints"/> puandan fazla artış yoksa null.
    /// </summary>
    public static string? DiskTrend(IReadOnlyList<DaySample> samples, DateOnly today)
    {
        var recent = samples
            .Where(s => s.Day <= today && s.Day >= today.AddDays(-TrendWindowDays))
            .OrderBy(s => s.Day)
            .ToList();
        if (recent.Count < 2) return null;

        var first = recent[0];
        var last = recent[^1];
        int days = last.Day.DayNumber - first.Day.DayNumber;
        if (days < HealthHistory.MinTrendDays) return null;

        double rise = last.DiskUsedPercent - first.DiskUsedPercent;
        return rise >= TrendRisePoints ? $"Son {days} günde disk %{rise:0} daha doldu" : null;
    }

    /// <summary>Hızlı Başlangıç açıksa "Kapat" süreyi sıfırlamaz; kullanıcıya bunu söyleyen uyarı gösterilir.</summary>
    public static string? Uptime(HealthSnapshot s) =>
        s.Uptime.TotalDays < UptimeDays ? null
        : s.FastStartup ? "Hızlı Başlangıç açık: tam kapanmıyor"
        : "Yeniden başlatmanız önerilir";

    public static string? Startup(HealthSnapshot s) =>
        s.StartupCount >= StartupItems ? "Fazla; Görev Yöneticisi → Başlangıç" : null;
}
