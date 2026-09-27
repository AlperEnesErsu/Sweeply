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

    public static string? Uptime(HealthSnapshot s) =>
        s.Uptime.TotalDays >= UptimeDays ? "Yeniden başlatmanız önerilir" : null;

    public static string? Startup(HealthSnapshot s) =>
        s.StartupCount >= StartupItems ? "Fazla; Görev Yöneticisi → Başlangıç" : null;
}
