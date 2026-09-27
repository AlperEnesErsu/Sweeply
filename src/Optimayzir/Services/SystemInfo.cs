using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Optimayzir.Services;

public sealed record HealthSnapshot(
    long RamTotal,
    long RamAvailable,
    int RamLoad,
    string DiskName,
    long DiskTotal,
    long DiskFree,
    TimeSpan Uptime,
    int StartupCount)
{
    public long RamUsed => RamTotal - RamAvailable;
    public double DiskUsedPercent => DiskTotal == 0 ? 0 : 100.0 * (DiskTotal - DiskFree) / DiskTotal;
}

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
            TimeSpan.FromMilliseconds(Environment.TickCount64),
            CountStartupItems());
    }

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

    /// <summary>Görev Yöneticisi'nde "Etkin" görünen başlangıç öğelerinin sayısı (Run anahtarları + Başlangıç klasörleri).</summary>
    static int CountStartupItems()
    {
        int count = 0;
        count += CountRunKey(Registry.CurrentUser);
        count += CountRunKey(Registry.LocalMachine);
        count += CountStartupFolder(Environment.GetFolderPath(Environment.SpecialFolder.Startup), Registry.CurrentUser);
        count += CountStartupFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), Registry.LocalMachine);
        return count;
    }

    static int CountRunKey(RegistryKey hive)
    {
        try
        {
            using var key = hive.OpenSubKey(RunKey);
            return key?.GetValueNames().Count(n => n.Length > 0 && !IsDisabled(hive, "Run", n)) ?? 0;
        }
        catch { return 0; }
    }

    static int CountStartupFolder(string folder, RegistryKey hive)
    {
        try
        {
            if (!Directory.Exists(folder)) return 0;
            return Directory.EnumerateFiles(folder)
                .Select(Path.GetFileName)
                .Count(n => n != null && !n.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) && !IsDisabled(hive, "StartupFolder", n));
        }
        catch { return 0; }
    }

    // StartupApproved altında ilk baytı tek sayı olan girdiler kullanıcı tarafından devre dışı bırakılmıştır.
    static bool IsDisabled(RegistryKey hive, string subKey, string name)
    {
        try
        {
            using var key = hive.OpenSubKey(ApprovedKey + subKey);
            return key?.GetValue(name) is byte[] { Length: > 0 } data && (data[0] & 1) == 1;
        }
        catch { return false; }
    }
}
