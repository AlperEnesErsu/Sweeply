using System.Text.Json;

namespace Sweeply.Services;

public enum RunOutcome { Done, Failed, Skipped, NotRun }

/// <param name="Message">Görevin sonucu ya da neden çalışmadığı.</param>
public sealed record RunLogEntry(string Title, RunOutcome Outcome, string? Message);

/// <summary>
/// Zamanlanmış (Görev Zamanlayıcı'dan açılan) son temizliğin sonucu. Kullanıcı o sırada bilgisayarın başında olmayabilir;
/// bir sonraki normal açılışta bu kayıt bir kez gösterilir. %LOCALAPPDATA%\Sweeply\last-run.json altında tutulur.
/// </summary>
public sealed class RunLog
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public DateTime Time { get; set; }
    public long DiskFreed { get; set; }
    public long RamFreed { get; set; }
    public bool Cancelled { get; set; }
    public List<RunLogEntry> Entries { get; set; } = [];

    /// <summary>Kullanıcı sonucu gördü mü? Görüldüyse tekrar gösterilmez.</summary>
    public bool Seen { get; set; }

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sweeply", "last-run.json");

    /// <returns>Kayıt yoksa veya okunamıyorsa null.</returns>
    public static RunLog? Load(string? path = null)
    {
        try
        {
            var file = path ?? DefaultPath;
            if (!File.Exists(file)) return null;
            var log = JsonSerializer.Deserialize<RunLog>(File.ReadAllText(file));
            if (log != null) log.Entries ??= [];
            return log;
        }
        catch
        {
            return null;
        }
    }

    public bool Save(string? path = null)
    {
        try
        {
            var file = path ?? DefaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, JsonSerializer.Serialize(this, Json));
            return true;
        }
        catch
        {
            return false;
        }
    }
}
