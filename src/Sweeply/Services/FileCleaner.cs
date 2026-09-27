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

    public void Add(CleanStats other)
    {
        Freed += other.Freed;
        Deleted += other.Deleted;
        KeptNew += other.KeptNew;
        Locked += other.Locked;
        LockedBytes += other.LockedBytes;
    }
}

/// <summary>
/// Bir klasörün içeriğini (klasörün kendisini değil) güvenli şekilde temizler.
/// Kullanımdaki dosyalar ve <c>minAge</c>'den yeni dosyalar atlanır; junction/symlink'lerin içine girilmez.
/// </summary>
public static class FileCleaner
{
    static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    public static long Measure(string path, TimeSpan minAge = default)
    {
        if (!Directory.Exists(path)) return 0;
        var cutoff = DateTime.UtcNow - minAge;
        long total = 0;
        try
        {
            foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", Options))
            {
                try
                {
                    if (file.LastWriteTimeUtc <= cutoff) total += file.Length;
                }
                catch { }
            }
        }
        catch { }
        return total;
    }

    public static CleanStats Clean(string path, TimeSpan minAge = default)
    {
        var stats = new CleanStats();
        if (!Directory.Exists(path)) return stats;
        var cutoff = DateTime.UtcNow - minAge;
        var root = new DirectoryInfo(path);

        List<FileInfo> files;
        try { files = root.EnumerateFiles("*", Options).ToList(); }
        catch { return stats; }

        foreach (var file in files)
        {
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
        }

        // Boşalan alt klasörleri en derinden başlayarak kaldır.
        try
        {
            foreach (var dir in root.EnumerateDirectories("*", Options).OrderByDescending(d => d.FullName.Length).ToList())
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

    /// <summary>Kullanıcıya gösterilecek sonuç cümlesi. Yeni dosyaların korunması normal olduğu için sadece kilitli dosyalardan bahsedilir.</summary>
    public static string Describe(CleanStats stats)
    {
        string main = stats.Freed > 0 ? $"{Format.Bytes(stats.Freed)} silindi" : "Silinecek dosya yoktu";
        if (stats.Locked > 0)
            main += $" · {Format.Bytes(stats.LockedBytes)} kullanımda olduğu için silinemedi";
        return main;
    }
}
