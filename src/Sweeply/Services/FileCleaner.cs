using Sweeply.Tasks;

namespace Sweeply.Services;

public sealed class CleanStats
{
    public long Freed;
    public int Deleted;

    /// <summary><c>minAge</c>'den yeni olduğu için bilerek korunan dosyalar (ör. çalışan bir kurulumun dosyaları).</summary>
    public int KeptNew;

    /// <summary>Silinmesi gerekirken kullanımda olduğu veya izin verilmediği için silinemeyen dosyalar.</summary>
    public int Locked;
    public long LockedBytes;

    public bool Cancelled;

    public void Add(CleanStats other)
    {
        Freed += other.Freed;
        Deleted += other.Deleted;
        KeptNew += other.KeptNew;
        Locked += other.Locked;
        LockedBytes += other.LockedBytes;
        Cancelled |= other.Cancelled;
    }
}

/// <summary>
/// Bir klasörün içeriğini (klasörün kendisini değil) güvenli şekilde temizler.
/// Kullanımdaki dosyalar ve <c>minAge</c>'den yeni dosyalar atlanır; junction/symlink'lerin içine girilmez.
/// </summary>
public static class FileCleaner
{
    static readonly EnumerationOptions Recursive = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    static readonly EnumerationOptions TopLevel = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    // İlerleme bu kadar dosyada bir veya bu süre dolunca bildirilir; arayüzü boğmamak için.
    const int ReportEveryFiles = 200;
    static readonly TimeSpan ReportEvery = TimeSpan.FromMilliseconds(120);

    public static long Measure(string path, TimeSpan minAge = default)
    {
        if (!Directory.Exists(path)) return 0;
        return Tally(new DirectoryInfo(path), DateTime.UtcNow - minAge).Bytes;
    }

    /// <param name="progress">Bu klasör için o ana kadarki toplamı bildirir.</param>
    public static CleanStats Clean(string path, TimeSpan minAge = default, IProgress<CleanProgress>? progress = null, CancellationToken ct = default)
    {
        var stats = new CleanStats();
        if (!Directory.Exists(path)) return stats;
        var cutoff = DateTime.UtcNow - minAge;
        var root = new DirectoryInfo(path);

        List<FileInfo> files;
        try { files = root.EnumerateFiles("*", Recursive).ToList(); }
        catch { return stats; }

        var lastReport = DateTime.UtcNow;
        int sinceReport = 0;
        foreach (var file in files)
        {
            if (ct.IsCancellationRequested)
            {
                stats.Cancelled = true;
                break;
            }

            long length = 0;
            try
            {
                if (file.LastWriteTimeUtc > cutoff)
                {
                    stats.KeptNew++;
                    continue;
                }
                length = file.Length;
                if (file.IsReadOnly) file.IsReadOnly = false;
                file.Delete();
                stats.Freed += length;
                stats.Deleted++;
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            catch
            {
                stats.Locked++;
                stats.LockedBytes += length;
            }

            if (progress != null && (++sinceReport >= ReportEveryFiles || DateTime.UtcNow - lastReport >= ReportEvery))
            {
                progress.Report(new CleanProgress(stats.Deleted, stats.Freed));
                sinceReport = 0;
                lastReport = DateTime.UtcNow;
            }
        }
        progress?.Report(new CleanProgress(stats.Deleted, stats.Freed));

        // Boşalan alt klasörleri en derinden başlayarak kaldır.
        try
        {
            foreach (var dir in root.EnumerateDirectories("*", Recursive).OrderByDescending(d => d.FullName.Length).ToList())
            {
                try
                {
                    if (!dir.EnumerateFileSystemInfos().Any()) dir.Delete();
                }
                catch { }
            }
        }
        catch { }

        return stats;
    }

    /// <summary>
    /// Silinecekleri klasörün ilk seviyesine göre gruplar (ör. Temp\kurulum-artigi: 8 GB, 1.200 dosya).
    /// Hiçbir şey silmez; sadece <c>minAge</c>'den eski dosyaları sayar.
    /// </summary>
    public static List<PreviewEntry> Preview(string path, TimeSpan minAge = default)
    {
        var entries = new List<PreviewEntry>();
        if (!Directory.Exists(path)) return entries;
        var cutoff = DateTime.UtcNow - minAge;

        try
        {
            foreach (var child in new DirectoryInfo(path).EnumerateFileSystemInfos("*", TopLevel))
            {
                try
                {
                    var (bytes, files) = child switch
                    {
                        DirectoryInfo dir => Tally(dir, cutoff),
                        FileInfo file when file.LastWriteTimeUtc <= cutoff => (file.Length, 1),
                        _ => (0L, 0),
                    };
                    if (files > 0) entries.Add(new PreviewEntry(child.FullName, bytes, files));
                }
                catch { }
            }
        }
        catch { }

        return entries.OrderByDescending(e => e.Bytes).ToList();
    }

    static (long Bytes, int Files) Tally(DirectoryInfo dir, DateTime cutoff)
    {
        long bytes = 0;
        int files = 0;
        try
        {
            foreach (var file in dir.EnumerateFiles("*", Recursive))
            {
                try
                {
                    if (file.LastWriteTimeUtc > cutoff) continue;
                    bytes += file.Length;
                    files++;
                }
                catch { }
            }
        }
        catch { }
        return (bytes, files);
    }

    /// <summary>Kullanıcıya gösterilecek sonuç cümlesi. Yeni dosyaların korunması normal olduğu için sadece kilitli dosyalardan bahsedilir.</summary>
    public static string Describe(CleanStats stats)
    {
        string main = stats.Freed > 0 ? $"{Format.Bytes(stats.Freed)} silindi"
            : stats.Cancelled ? "Durduruldu"
            : "Silinecek dosya yoktu";
        if (stats.Locked > 0)
            main += $" · {Format.Bytes(stats.LockedBytes)} kullanımda olduğu için silinemedi";
        if (stats.Cancelled && stats.Freed > 0)
            main += " · durduruldu";
        return main;
    }
}
