namespace Optimayzir.Services;

public sealed class CleanStats
{
    public long Freed;
    public int Deleted;
    public int Skipped;
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
            try
            {
                if (file.LastWriteTimeUtc > cutoff)
                {
                    stats.Skipped++;
                    continue;
                }
                long length = file.Length;
                if (file.IsReadOnly) file.IsReadOnly = false;
                file.Delete();
                stats.Freed += length;
                stats.Deleted++;
            }
            catch
            {
                stats.Skipped++;
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
}
