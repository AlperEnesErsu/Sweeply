using Sweeply.Services;

namespace Sweeply.Tasks;

public sealed class BrowserCacheTask : ICleanupTask
{
    internal sealed record Browser(string Name, string ProcessName, string UserDataPath);

    // Sadece önbellek klasörleri: geçmiş, şifreler, çerezler ve açık sekmeler bunların dışında.
    internal static readonly string[] CacheFolders = ["Cache", "Code Cache", "GPUCache"];

    readonly Browser[] _browsers;
    readonly Func<string, bool> _isRunning;

    public BrowserCacheTask() : this(DefaultBrowsers(), name => CommandRunner.CountProcesses(name) > 0)
    {
    }

    internal BrowserCacheTask(Browser[] browsers, Func<string, bool> isRunning)
    {
        _browsers = browsers;
        _isRunning = isRunning;
    }

    static Browser[] DefaultBrowsers()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return
        [
            new("Chrome", "chrome", Path.Combine(local, @"Google\Chrome\User Data")),
            new("Edge", "msedge", Path.Combine(local, @"Microsoft\Edge\User Data")),
            new("Brave", "brave", Path.Combine(local, @"BraveSoftware\Brave-Browser\User Data")),
        ];
    }

    public string Title => "Tarayıcı önbellekleri";
    public string Description => "Chrome, Edge ve Brave önbellekleri. Geçmiş, şifreler ve oturumlar silinmez. Açık tarayıcılar atlanır.";
    public TaskKind Kind => TaskKind.Disk;
    public bool EnabledByDefault => true;

    /// <summary>Bir tarayıcı veri klasöründeki kullanıcı profillerinin (Default, Profile N) önbellek klasörleri.</summary>
    internal static IEnumerable<string> CachePaths(string userDataPath)
    {
        if (!Directory.Exists(userDataPath)) yield break;
        foreach (var profile in Directory.EnumerateDirectories(userDataPath))
        {
            var name = Path.GetFileName(profile);
            if (name != "Default" && !name.StartsWith("Profile ", StringComparison.Ordinal)) continue;
            foreach (var cache in CacheFolders) yield return Path.Combine(profile, cache);
        }
    }

    (List<Browser> Closed, List<Browser> Open) Split()
    {
        var installed = _browsers.Where(b => Directory.Exists(b.UserDataPath)).ToList();
        var open = installed.Where(b => _isRunning(b.ProcessName)).ToList();
        return (installed.Except(open).ToList(), open);
    }

    static string Names(List<Browser> browsers) => string.Join(", ", browsers.Select(b => b.Name));

    public Task<Analysis> AnalyzeAsync(CancellationToken ct) => Task.Run(() =>
    {
        var (closed, open) = Split();
        string? note = open.Count == 0 ? null : $"{Names(open)} açık; kapatınca temizlenebilir.";
        if (closed.Count == 0)
            return new Analysis(false, 0, open.Count == 0 ? "Desteklenen tarayıcı bulunamadı." : note);

        long bytes = closed.SelectMany(b => CachePaths(b.UserDataPath)).Sum(p => FileCleaner.Measure(p));
        return new Analysis(true, bytes, note);
    }, ct);

    public Task<TaskResult> RunAsync(CancellationToken ct) => Task.Run(() =>
    {
        var (closed, open) = Split();
        var total = new CleanStats();
        foreach (var path in closed.SelectMany(b => CachePaths(b.UserDataPath))) total.Add(FileCleaner.Clean(path));
        string message = FileCleaner.Describe(total);
        if (open.Count > 0) message += $" · {Names(open)} açık olduğu için atlandı";
        return new TaskResult(total.Freed, message);
    }, ct);
}
