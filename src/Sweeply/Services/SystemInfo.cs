using System.Runtime.InteropServices;

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
            StartupService.GetItems().Count,
            BootHistory.FastStartupEnabled());
    }
}
