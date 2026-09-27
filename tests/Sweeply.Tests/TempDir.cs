namespace Sweeply.Tests;

/// <summary>Her test için ayrı, test bitince silinen geçici bir klasör.</summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sweeply-tests", Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    /// <summary>İstenen boyutta ve yaşta bir dosya oluşturur, tam yolunu döndürür.</summary>
    public string File(string relative, int bytes, TimeSpan age = default)
    {
        var full = System.IO.Path.Combine(Path, relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllBytes(full, new byte[bytes]);
        System.IO.File.SetLastWriteTimeUtc(full, DateTime.UtcNow - age);
        return full;
    }

    public string Dir(string relative)
    {
        var full = System.IO.Path.Combine(Path, relative);
        Directory.CreateDirectory(full);
        return full;
    }

    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
                System.IO.File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(Path, recursive: true);
        }
        catch { }
    }
}
