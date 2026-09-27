using Optimayzir.Services;

namespace Optimayzir.Tasks;

public sealed class BrowserCacheTask : ICleanupTask
{
    sealed record Browser(string Name, string ProcessName, string UserDataPath);

    static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    static readonly Browser[] Browsers =
    [
        new("Chrome", "chrome", Path.Combine(LocalAppData, @"Google\Chrome\User Data")),
        new("Edge", "msedge", Path.Combine(LocalAppData, @"Microsoft\Edge\User Data")),
        new("Brave", "brave", Path.Combine(LocalAppData, @"BraveSoftware\Brave-Browser\User Data")),
    ];

    // Sadece önbellek klasörleri: geçmiş, şifreler, çerezler ve açık sekmeler bunların dışında.
    static readonly string[] CacheFolders = ["Cache", "Code Cache", "GPUCache"];

    public string Title => "Tarayıcı önbellekleri";
    public string Description => "Chrome, Edge ve Brave önbellekleri. Geçmiş, şifreler ve oturumlar silinmez. Açık tarayıcılar atlanır.";
    public TaskKind Kind => TaskKind.Disk;
    public bool EnabledByDefault => true;

    static IEnumerable<string> CachePaths(Browser browser)
    {
        if (!Directory.Exists(browser.UserDataPath)) yield break;
        foreach (var profile in Directory.EnumerateDirectories(browser.UserDataPath))
        {
            var name = Path.GetFileName(profile);
            if (name != "Default" && !name.StartsWith("Profile ", StringComparison.Ordinal)) continue;
            foreach (var cache in CacheFolders) yield return Path.Combine(profile, cache);
        }
    }

    static (List<Browser> Closed, List<Browser> Open) Split()
    {
        var installed = Browsers.Where(b => Directory.Exists(b.UserDataPath)).ToList();
        var open = installed.Where(b => CommandRunner.CountProcesses(b.ProcessName) > 0).ToList();
        return (installed.Except(open).ToList(), open);
    }

    static string? OpenNote(List<Browser> open) =>
        open.Count == 0 ? null : $"{string.Join(", ", open.Select(b => b.Name))} açık; kapatınca temizlenebilir.";

    public Task<Analysis> AnalyzeAsync(CancellationToken ct) => Task.Run(() =>
    {
        var (closed, open) = Split();
        if (closed.Count == 0 && open.Count == 0) return new Analysis(false, 0, "Desteklenen tarayıcı bulunamadı.");
        if (closed.Count == 0) return new Analysis(false, 0, OpenNote(open));
        long bytes = closed.SelectMany(CachePaths).Sum(p => FileCleaner.Measure(p));
        return new Analysis(true, bytes, OpenNote(open));
    }, ct);

    public Task<TaskResult> RunAsync(CancellationToken ct) => Task.Run(() =>
    {
        var (closed, open) = Split();
        long freed = closed.SelectMany(CachePaths).Sum(p => FileCleaner.Clean(p).Freed);
        string message = $"{Format.Bytes(freed)} silindi";
        if (open.Count > 0) message += $" · {string.Join(", ", open.Select(b => b.Name))} açık olduğu için atlandı";
        return new TaskResult(freed, message);
    }, ct);
}
