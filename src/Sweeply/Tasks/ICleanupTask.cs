namespace Sweeply.Tasks;

public enum TaskKind { Disk, Ram }

/// <param name="Available">Görev bu sistemde çalıştırılabilir mi (ör. Docker kurulu ve açık mı)?</param>
/// <param name="Bytes">Kazanılabilecek tahmini alan; bilinmiyorsa -1.</param>
/// <param name="Note">Kullanıcıya gösterilecek kısa uyarı veya açıklama.</param>
public sealed record Analysis(bool Available, long Bytes, string? Note = null);

public sealed record TaskResult(long FreedBytes, string Message);

public interface ICleanupTask
{
    string Title { get; }
    string Description { get; }
    TaskKind Kind { get; }
    bool EnabledByDefault { get; }

    Task<Analysis> AnalyzeAsync(CancellationToken ct);
    Task<TaskResult> RunAsync(CancellationToken ct);
}
