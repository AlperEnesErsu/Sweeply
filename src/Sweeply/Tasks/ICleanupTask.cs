namespace Sweeply.Tasks;

public enum TaskKind { Disk, Ram }

/// <summary>Görev listesindeki bölüm başlığı. Sıra, listedeki sırayı belirler.</summary>
public enum TaskGroup { System, Apps, Developer, Memory }

/// <param name="Available">Görev şu an çalıştırılabilir mi (ör. Docker açık mı, yönetici izni var mı)?</param>
/// <param name="Bytes">Kazanılabilecek tahmini alan; bilinmiyorsa -1.</param>
/// <param name="Note">Kullanıcıya gösterilecek kısa uyarı veya açıklama.</param>
public sealed record Analysis(bool Available, long Bytes, string? Note = null);

public sealed record TaskResult(long FreedBytes, string Message);

/// <summary>Bir görev çalışırken o ana kadar silinen dosya sayısı ve boşalan alan.</summary>
public readonly record struct CleanProgress(int Files, long Bytes);

/// <summary>Önizlemede bir satır: silinecek bir klasör/dosya ya da çalıştırılacak bir komut.</summary>
public sealed record PreviewEntry(string Label, long Bytes, int Files, bool IsCommand = false);

public sealed record Preview(IReadOnlyList<PreviewEntry> Entries, string? Note = null)
{
    public static readonly Preview Empty = new([]);
}

public interface ICleanupTask
{
    string Title { get; }
    string Description { get; }
    TaskKind Kind { get; }
    TaskGroup Group { get; }
    bool EnabledByDefault { get; }

    Task<Analysis> AnalyzeAsync(CancellationToken ct);

    /// <summary>Hiçbir şeyi silmeden, çalıştırılınca neyin silineceğini listeler.</summary>
    Task<Preview> PreviewAsync(CancellationToken ct);

    Task<TaskResult> RunAsync(IProgress<CleanProgress>? progress, CancellationToken ct);
}
