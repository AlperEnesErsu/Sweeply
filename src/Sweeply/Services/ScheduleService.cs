using System.Globalization;

namespace Sweeply.Services;

/// <summary>
/// Haftalık otomatik temizlik: Windows Görev Zamanlayıcı'da, uygulamayı <c>--auto --scheduled</c> ile açan bir görev.
/// Görev kullanıcının kendi hesabında, yönetici izni olmadan oluşturulur.
/// </summary>
public static class ScheduleService
{
    public const string TaskName = "Sweeply Otomatik Temizlik";
    public const DayOfWeek DefaultDay = DayOfWeek.Sunday;
    public const string DefaultTime = "10:00";

    /// <summary>Zamanlama penceresinde sunulan saatler.</summary>
    public static readonly string[] Times = ["08:00", "10:00", "12:00", "14:00", "18:00", "21:00"];

    /// <summary>Görev Zamanlayıcı'dan açıldığını belirten parametre; çalışmanın sonucu <see cref="RunLog"/>'a yazılır.</summary>
    public const string ScheduledFlag = "--scheduled";

    /// <summary>Görevin yalnızca kurulu <c>Sweeply.exe</c>'den oluşturulmasına izin verilir; "dotnet run" gibi yollar kabul edilmez.</summary>
    public static string? CheckExecutable(string? exe) =>
        string.IsNullOrEmpty(exe) || !Path.GetFileName(exe).Equals("Sweeply.exe", StringComparison.OrdinalIgnoreCase)
            ? "Otomatik temizlik yalnızca kurulu Sweeply.exe ile zamanlanabilir."
            : null;

    /// <summary>"HH:mm" biçimindeki saati okur; geçersizse null.</summary>
    public static TimeOnly? ParseTime(string? text) =>
        TimeOnly.TryParseExact(text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : null;

    /// <summary>schtasks'ın beklediği gün kısaltması (MON, TUE, …). Bölgesel ayarlardan bağımsızdır.</summary>
    internal static string DayCode(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "MON",
        DayOfWeek.Tuesday => "TUE",
        DayOfWeek.Wednesday => "WED",
        DayOfWeek.Thursday => "THU",
        DayOfWeek.Friday => "FRI",
        DayOfWeek.Saturday => "SAT",
        _ => "SUN",
    };

    /// <summary>Pazartesiden başlayan hafta; zamanlama penceresindeki sıra.</summary>
    public static readonly DayOfWeek[] Week =
    [
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday,
    ];

    /// <summary>Kullanıcıya gösterilen gün adı ("pazar"). Sistem diline bağlı olmasın diye sabittir.</summary>
    public static string DayName(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "pazartesi",
        DayOfWeek.Tuesday => "salı",
        DayOfWeek.Wednesday => "çarşamba",
        DayOfWeek.Thursday => "perşembe",
        DayOfWeek.Friday => "cuma",
        DayOfWeek.Saturday => "cumartesi",
        _ => "pazar",
    };

    /// <summary>"her pazar, saat 10:00". Ek almaz; saatin okunuşuna göre değişen "'da/'de" yanlış çıkmasın diye.</summary>
    public static string Describe(DayOfWeek day, TimeOnly time) =>
        $"her {DayName(day)}, saat {time.ToString("HH:mm", CultureInfo.InvariantCulture)}";

    /// <summary>schtasks argümanları. <c>/TR</c> değeri tırnaklı yolu ve parametreleri tek argüman olarak taşır.</summary>
    internal static string BuildCreateArguments(string exe, DayOfWeek day, TimeOnly time) =>
        $"/Create /TN \"{TaskName}\" /SC WEEKLY /D {DayCode(day)} /ST {time.ToString("HH:mm", CultureInfo.InvariantCulture)} " +
        $"/TR \"\\\"{exe}\\\" --auto {ScheduledFlag}\" /RL LIMITED /F";

    public static async Task<bool> IsScheduledAsync()
    {
        var (exit, _) = await CommandRunner.RunAsync("schtasks.exe", $"/Query /TN \"{TaskName}\"", TimeSpan.FromSeconds(30));
        return exit == 0;
    }

    /// <summary>Görevi oluşturur; varsa aynı adla üzerine yazar (<c>/F</c>), böylece gün/saat değişikliği de bununla yapılır.</summary>
    /// <returns>Oluşturulduysa null, aksi halde Windows'un çıktısı.</returns>
    public static async Task<string?> CreateAsync(string? exe, DayOfWeek day, TimeOnly time)
    {
        var problem = CheckExecutable(exe);
        if (problem != null) return problem;

        var (exit, output) = await CommandRunner.RunAsync("schtasks.exe", BuildCreateArguments(exe!, day, time), TimeSpan.FromSeconds(30));
        return exit == 0 ? null : output.Trim();
    }

    /// <returns>Silindiyse null, aksi halde Windows'un çıktısı.</returns>
    public static async Task<string?> RemoveAsync()
    {
        var (exit, output) = await CommandRunner.RunAsync("schtasks.exe", $"/Delete /TN \"{TaskName}\" /F", TimeSpan.FromSeconds(30));
        return exit == 0 ? null : output.Trim();
    }
}
