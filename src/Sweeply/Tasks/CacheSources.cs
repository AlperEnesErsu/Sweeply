namespace Sweeply.Tasks;

/// <summary>Bir uygulamanın temizlenebilecek tek bir önbelleği.</summary>
public abstract record CacheSource;

/// <summary>İçeriği doğrudan silinen klasör. <paramref name="MinAge"/>'den yeni dosyalar korunur.</summary>
public sealed record FolderSource(string Path, TimeSpan MinAge = default) : CacheSource;

/// <summary>
/// Aracın kendi temizleme komutuyla boşaltılan önbellek (ör. <c>npm cache clean --force</c>).
/// Kazanç, <paramref name="MeasurePaths"/> klasörlerinin komuttan önceki ve sonraki boyut farkıdır.
/// Araç kurulu değilse ve <paramref name="FolderFallback"/> açıksa bu klasörler doğrudan temizlenir.
/// </summary>
public sealed record CommandSource(
    string Executable,
    string Arguments,
    string[] MeasurePaths,
    bool FolderFallback = true,
    TimeSpan? Timeout = null) : CacheSource
{
    public string Display => $"{System.IO.Path.GetFileNameWithoutExtension(Executable)} {Arguments}";
}

/// <summary>
/// Temizlenecek bir uygulama. <paramref name="Processes"/> içinden biri çalışıyorsa uygulama bu turda atlanır;
/// açık bir programın önbelleğini silmek onu bozabilir.
/// </summary>
public sealed record CacheApp(string Name, string[] Processes, IReadOnlyList<CacheSource> Sources);
