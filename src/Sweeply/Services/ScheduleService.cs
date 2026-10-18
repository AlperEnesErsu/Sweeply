namespace Sweeply.Services;

/// <summary>
/// Haftalık otomatik temizlik: Windows Görev Zamanlayıcı'da, uygulamayı <c>--auto</c> ile açan bir görev.
/// Görev kullanıcının kendi hesabında, yönetici izni olmadan oluşturulur.
/// </summary>
public static class ScheduleService
{
    public const string TaskName = "Sweeply Otomatik Temizlik";
    public const string Day = "SUN";
    public const string Time = "10:00";

    /// <summary>Görevin yalnızca kurulu <c>Sweeply.exe</c>'den oluşturulmasına izin verilir; "dotnet run" gibi yollar kabul edilmez.</summary>
    public static string? CheckExecutable(string? exe) =>
        string.IsNullOrEmpty(exe) || !Path.GetFileName(exe).Equals("Sweeply.exe", StringComparison.OrdinalIgnoreCase)
            ? "Otomatik temizlik yalnızca kurulu Sweeply.exe ile zamanlanabilir."
            : null;

    /// <summary>schtasks argümanları. <c>/TR</c> değeri tırnaklı yolu ve <c>--auto</c> parametresini tek argüman olarak taşır.</summary>
    internal static string BuildCreateArguments(string exe) =>
        $"/Create /TN \"{TaskName}\" /SC WEEKLY /D {Day} /ST {Time} /TR \"\\\"{exe}\\\" --auto\" /RL LIMITED /F";

    public static async Task<bool> IsScheduledAsync()
    {
        var (exit, _) = await CommandRunner.RunAsync("schtasks.exe", $"/Query /TN \"{TaskName}\"", TimeSpan.FromSeconds(30));
        return exit == 0;
    }

    /// <returns>Oluşturulduysa null, aksi halde Windows'un çıktısı.</returns>
    public static async Task<string?> CreateAsync(string? exe)
    {
        var problem = CheckExecutable(exe);
        if (problem != null) return problem;

        var (exit, output) = await CommandRunner.RunAsync("schtasks.exe", BuildCreateArguments(exe!), TimeSpan.FromSeconds(30));
        return exit == 0 ? null : output.Trim();
    }

    /// <returns>Silindiyse null, aksi halde Windows'un çıktısı.</returns>
    public static async Task<string?> RemoveAsync()
    {
        var (exit, output) = await CommandRunner.RunAsync("schtasks.exe", $"/Delete /TN \"{TaskName}\" /F", TimeSpan.FromSeconds(30));
        return exit == 0 ? null : output.Trim();
    }
}
