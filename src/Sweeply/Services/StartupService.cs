using Microsoft.Win32;

namespace Sweeply.Services;

/// <param name="Command">Çalıştırılan komut veya kısayol yolu.</param>
/// <param name="Source">Nereden başlatıldığı (kayıt defteri / Başlangıç klasörü).</param>
/// <param name="Enabled">Görev Yöneticisi'nde "Etkin" mi.</param>
public sealed record StartupItem(string Name, string Command, string Source, bool Enabled = true)
{
    /// <summary>Run anahtarında mı yoksa Başlangıç klasöründe mi olduğu.</summary>
    public bool FromFolder => Source.EndsWith("Başlangıç klasörü", StringComparison.Ordinal);

    /// <summary>StartupApproved'daki ad: Run değerinde ad, Başlangıç klasöründe uzantılı dosya adı.</summary>
    public string ValueName => FromFolder ? Path.GetFileName(Command) : Name;

    /// <summary>Kullanıcı bazında mı (HKCU / Kullanıcı Başlangıç klasörü) yoksa tüm kullanıcılar için mi.</summary>
    public bool PerUser => Source.StartsWith("Kullanıcı", StringComparison.Ordinal);
}

/// <summary>
/// Windows'un "Etkin/Devre dışı" durumunu tuttuğu StartupApproved anahtarını okur ve yazar.
/// Görev Yöneticisi'nin Başlangıç sekmesiyle aynı veriyi kullanır; öğe silinmez, sadece açılışta çalıştırılmaz.
/// </summary>
public static class StartupService
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ApprovedRoot = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

    /// <summary>Görev Yöneticisi'nde "Etkin" görünen başlangıç öğeleri (Run anahtarları + Başlangıç klasörleri).</summary>
    public static List<StartupItem> GetItems(bool includeDisabled = false)
    {
        var items = new List<StartupItem>();
        items.AddRange(FromRunKey(Registry.CurrentUser, "Kullanıcı · kayıt defteri"));
        items.AddRange(FromRunKey(Registry.LocalMachine, "Tüm kullanıcılar · kayıt defteri"));
        items.AddRange(FromStartupFolder(Environment.GetFolderPath(Environment.SpecialFolder.Startup), Registry.CurrentUser, "Kullanıcı · Başlangıç klasörü"));
        items.AddRange(FromStartupFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), Registry.LocalMachine, "Tüm kullanıcılar · Başlangıç klasörü"));
        return items
            .Where(i => includeDisabled || i.Enabled)
            .OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Öğeyi açılışta çalışacak ya da çalışmayacak şekilde işaretler. Yönetici izni gerekiyorsa false döner.</summary>
    public static bool SetEnabled(StartupItem item, bool enabled)
    {
        try
        {
            var hive = item.PerUser ? Registry.CurrentUser : Registry.LocalMachine;
            string subKey = ApprovedRoot + (item.FromFolder ? "StartupFolder" : "Run");
            using var key = hive.CreateSubKey(subKey, writable: true);
            key.SetValue(item.ValueName, ApprovedValue(enabled), RegistryValueKind.Binary);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Windows'un yazdığı 12 baytlık biçim: ilk bayt 0x02 etkin, 0x03 devre dışı; devre dışı olan için
    /// bayt 4–11 devre dışı bırakılma zamanını (FILETIME) tutar.
    /// </summary>
    internal static byte[] ApprovedValue(bool enabled, DateTime? when = null)
    {
        var data = new byte[12];
        data[0] = enabled ? (byte)0x02 : (byte)0x03;
        if (!enabled)
            BitConverter.GetBytes((when ?? DateTime.UtcNow).ToFileTimeUtc()).CopyTo(data, 4);
        return data;
    }

    /// <summary>StartupApproved verisinde ilk bayt tek ise öğe devre dışıdır.</summary>
    internal static bool IsDisabledFlag(byte[]? data) => data is { Length: > 0 } && (data[0] & 1) == 1;

    static IEnumerable<StartupItem> FromRunKey(RegistryKey hive, string source)
    {
        try
        {
            using var key = hive.OpenSubKey(RunKey);
            if (key == null) return [];
            return key.GetValueNames()
                .Where(n => n.Length > 0)
                .Select(n => new StartupItem(n, key.GetValue(n)?.ToString() ?? "", source, !IsDisabled(hive, "Run", n)))
                .ToList();
        }
        catch { return []; }
    }

    static IEnumerable<StartupItem> FromStartupFolder(string folder, RegistryKey hive, string source)
    {
        try
        {
            if (!Directory.Exists(folder)) return [];
            return Directory.EnumerateFiles(folder)
                .Select(Path.GetFileName)
                .Where(n => n != null && !n.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
                .Select(n => new StartupItem(Path.GetFileNameWithoutExtension(n!), Path.Combine(folder, n!), source, !IsDisabled(hive, "StartupFolder", n!)))
                .ToList();
        }
        catch { return []; }
    }

    static bool IsDisabled(RegistryKey hive, string subKey, string name)
    {
        try
        {
            using var key = hive.OpenSubKey(ApprovedRoot + subKey);
            return IsDisabledFlag(key?.GetValue(name) as byte[]);
        }
        catch { return false; }
    }
}
