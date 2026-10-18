namespace Sweeply.Services;

/// <summary>
/// Kullanıcının eklediği temizlik klasörlerinin güvenlik kuralları. README'deki söz gereği kişisel dosyalara
/// (Masaüstü, Belgeler, İndirilenler, Resimler...) ve sistem klasörlerine hiçbir yoldan dokunulmaz.
/// </summary>
public static class FolderRules
{
    /// <summary>Bu klasörlerin kendisi veya üstü temizlik klasörü olamaz (kullanıcı profili, kök, vb.).</summary>
    /// <param name="path">Eklenmek istenen klasör.</param>
    /// <param name="userProfile">Kullanıcı profili klasörü.</param>
    /// <param name="personal">Kişisel klasörler: içleri de dahil olmak üzere korunur.</param>
    /// <param name="system">Sistem klasörleri: içleri de dahil olmak üzere korunur.</param>
    /// <returns>Reddedilme nedeni; kabul edilebilirse null.</returns>
    public static string? Validate(string path, string userProfile, IEnumerable<string> personal, IEnumerable<string> system)
    {
        if (string.IsNullOrWhiteSpace(path)) return "Klasör seçilmedi.";

        string full;
        try { full = Path.GetFullPath(path); }
        catch { return "Geçersiz klasör yolu."; }

        if (!Directory.Exists(full)) return "Klasör bulunamadı.";
        if (Path.GetPathRoot(full) == full) return "Sürücünün kökü eklenemez.";

        // Bazı özel klasörler (ör. OneDrive dışındaki kurulumlarda) boş dönebilir; boş yollar karşılaştırmaya girmez.
        // Profilin içindeki her klasör serbesttir (ör. AppData); profilin kendisi ve üstleri ise korunur.
        var personalList = personal.Where(p => !string.IsNullOrEmpty(p)).ToList();
        if (personalList.Any(p => IsSameOrDescendant(full, p) || IsSameOrAncestor(full, p)) ||
            (!string.IsNullOrEmpty(userProfile) && IsSameOrAncestor(full, userProfile)))
            return "Kişisel klasörler (Masaüstü, Belgeler, İndirilenler vb.) veya bunları içeren klasörler eklenemez.";

        if (system.Where(s => !string.IsNullOrEmpty(s)).Any(s => IsSameOrDescendant(full, s) || IsSameOrAncestor(full, s)))
            return "Windows ve Program klasörleri eklenemez.";

        return null;
    }

    /// <summary>Gerçek kullanıcı klasörleriyle kontrol eder.</summary>
    public static string? Validate(string path)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var personal = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            Path.Combine(profile, "Downloads"),
        };
        var system = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        };
        return Validate(path, profile, personal, system);
    }

    static bool IsSameOrDescendant(string path, string parent) =>
        Norm(path).StartsWith(Norm(parent), StringComparison.OrdinalIgnoreCase);

    static bool IsSameOrAncestor(string path, string child) =>
        Norm(child).StartsWith(Norm(path), StringComparison.OrdinalIgnoreCase);

    // "C:\Users\a" ile "C:\Users\ab"yi karıştırmamak için sona ayraç eklenir.
    static string Norm(string p) => Path.GetFullPath(p).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
}
