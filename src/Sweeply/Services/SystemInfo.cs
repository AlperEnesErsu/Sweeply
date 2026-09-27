using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Sweeply.Services;

public sealed record HealthSnapshot(
    long RamTotal,
    long RamAvailable,
    int RamLoad,
    string DiskName,
    long DiskTotal,
    long DiskFree,
    TimeSpan Uptime,
    int StartupCount,
    bool FastStartup = false)
{
    public long RamUsed => RamTotal - RamAvailable;
    public double DiskUsedPercent => DiskTotal == 0 ? 0 : 100.0 * (DiskTotal - DiskFree) / DiskTotal;
}

/// <param name="Command">Çalıştırılan komut veya kısayol yolu.</param>
/// <param name="Source">Nereden başlatıldığı (kayıt defteri / Başlangıç klasörü).</param>
public sealed record StartupItem(string Name, string Command, string Source);

public static class SystemInfo
{
    [StructLayout(LayoutKind.Sequential)]
    struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);

    public static HealthSnapshot GetSnapshot()
    {
        var mem = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        GlobalMemoryStatusEx(ref mem);

        var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");

        return new HealthSnapshot(
            (long)mem.TotalPhys,
            (long)mem.AvailPhys,
            (int)mem.MemoryLoad,
            drive.Name.TrimEnd('\\'),
            drive.TotalSize,
            drive.AvailableFreeSpace,
            // Hızlı Başlangıç ile yapılan "kapat/aç" döngüleri bu süreyi sıfırlamaz; süre son tam açılıştan beri sayılır.
            TimeSpan.FromMilliseconds(Environment.TickCount64),
            GetStartupItems().Count,
            BootHistory.FastStartupEnabled());
    }

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

    /// <summary>Görev Yöneticisi'nde "Etkin" görünen başlangıç öğeleri (Run anahtarları + Başlangıç klasörleri).</summary>
    public static List<StartupItem> GetStartupItems()
    {
        var items = new List<StartupItem>();
        items.AddRange(FromRunKey(Registry.CurrentUser, "Kullanıcı · kayıt defteri"));
        items.AddRange(FromRunKey(Registry.LocalMachine, "Tüm kullanıcılar · kayıt defteri"));
        items.AddRange(FromStartupFolder(Environment.GetFolderPath(Environment.SpecialFolder.Startup), Registry.CurrentUser, "Kullanıcı · Başlangıç klasörü"));
        items.AddRange(FromStartupFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), Registry.LocalMachine, "Tüm kullanıcılar · Başlangıç klasörü"));
        return items.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    static IEnumerable<StartupItem> FromRunKey(RegistryKey hive, string source)
    {
        try
        {
            using var key = hive.OpenSubKey(RunKey);
            if (key == null) return [];
            return key.GetValueNames()
                .Where(n => n.Length > 0 && !IsDisabled(hive, "Run", n))
                .Select(n => new StartupItem(n, key.GetValue(n)?.ToString() ?? "", source))
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
                .Select(p => Path.GetFileName(p))
                .Where(n => !n.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) && !IsDisabled(hive, "StartupFolder", n))
                .Select(n => new StartupItem(Path.GetFileNameWithoutExtension(n), Path.Combine(folder, n), source))
                .ToList();
        }
        catch { return []; }
    }

    // StartupApproved altında ilk baytı tek sayı olan girdiler kullanıcı tarafından devre dışı bırakılmıştır.
    static bool IsDisabled(RegistryKey hive, string subKey, string name)
    {
        try
        {
            using var key = hive.OpenSubKey(ApprovedKey + subKey);
            return IsDisabledFlag(key?.GetValue(name) as byte[]);
        }
        catch { return false; }
    }

    internal static bool IsDisabledFlag(byte[]? data) => data is { Length: > 0 } && (data[0] & 1) == 1;
}
