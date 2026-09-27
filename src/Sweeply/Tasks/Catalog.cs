using Sweeply.Services;

namespace Sweeply.Tasks;

/// <summary>
/// Uygulamadaki tüm temizlik görevleri. Klasör kökleri <see cref="Roots"/> ile verilir, böylece testler
/// gerçek kullanıcı klasörleri yerine geçici klasörlerle çalışabilir.
/// </summary>
public static class Catalog
{
    public sealed record Roots(string Local, string Roaming, string Home, string Temp, string Windows)
    {
        public static Roots Current => new(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Path.GetTempPath(),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows));
    }

    /// <summary>Yeni geçici dosyalar çalışan bir kurulumun veya programın olabilir; onlara dokunulmaz.</summary>
    public static readonly TimeSpan TempMinAge = TimeSpan.FromHours(24);

    /// <summary>Chromium tabanlı tarayıcılarda ve Electron uygulamalarında güvenle silinebilen önbellekler. Çerez, geçmiş ve oturum bunların dışındadır.</summary>
    internal static readonly string[] ChromiumCacheFolders = ["Cache", "Code Cache", "GPUCache"];

    public static ICleanupTask[] All()
    {
        var r = Roots.Current;
        return
        [
            Temp(r), CrashDumps(r), WindowsUpdate(r), DeliveryOptimization(r),
            Browsers(r), AppCaches(r), Spotify(r),
            DevCaches(r), PackageStores(r), new DockerBuildCacheTask(),
            new WslShutdownTask(),
        ];
    }

    static CacheApp App(string name, string[] processes, IEnumerable<CacheSource> sources) => new(name, processes, sources.ToList());

    static IEnumerable<CacheSource> Folders(string root, params string[] names) =>
        names.Select(n => new FolderSource(Path.Combine(root, n)));

    // ---- Sistem -------------------------------------------------------------------------------

    public static CacheTask Temp(Roots r) => new()
    {
        Title = "Geçici dosyalar",
        Description = "Kullanıcı ve Windows Temp klasörlerindeki 24 saatten eski dosyalar. Kullanımdaki dosyalar atlanır.",
        Group = TaskGroup.System,
        Apps = () => [App("Temp", [], [new FolderSource(r.Temp, TempMinAge), new FolderSource(Path.Combine(r.Windows, "Temp"), TempMinAge)])],
    };

    public static CacheTask CrashDumps(Roots r) => new()
    {
        Title = "Çökme dökümleri ve hata raporları",
        Description = "Çöken programların bıraktığı döküm dosyaları ve gönderilmiş Windows hata raporları.",
        Group = TaskGroup.System,
        Apps = () => [App("Hata raporları", [], Folders(r.Local, "CrashDumps", @"Microsoft\Windows\WER\ReportArchive", @"Microsoft\Windows\WER\ReportQueue"))],
    };

    public static CacheTask WindowsUpdate(Roots r) => new()
    {
        Title = "Windows Update önbelleği",
        Description = "Kurulmuş güncellemelerin indirme artıkları. Temizlik sırasında Windows Update servisi kısa süre durdurulur.",
        Group = TaskGroup.System,
        RequiresAdmin = true,
        Apps = () => [App("Windows Update", [], [new FolderSource(Path.Combine(r.Windows, @"SoftwareDistribution\Download"))])],
        Before = async ct =>
        {
            await CommandRunner.RunAsync("net.exe", "stop wuauserv /y", TimeSpan.FromMinutes(1), ct);
            await CommandRunner.RunAsync("net.exe", "stop bits /y", TimeSpan.FromMinutes(1), ct);
        },
        After = async () =>
        {
            await CommandRunner.RunAsync("net.exe", "start bits", TimeSpan.FromMinutes(1));
            await CommandRunner.RunAsync("net.exe", "start wuauserv", TimeSpan.FromMinutes(1));
        },
    };

    public static CacheTask DeliveryOptimization(Roots r) => new()
    {
        Title = "Teslim İyileştirme önbelleği",
        Description = "Windows'un güncellemeleri diğer bilgisayarlarla paylaşmak için tuttuğu kopyalar.",
        Group = TaskGroup.System,
        RequiresAdmin = true,
        Apps = () =>
        [
            App("Teslim İyileştirme", [],
            [
                new CommandSource("powershell.exe", "-NoProfile -Command Delete-DeliveryOptimizationCache -Force",
                    [Path.Combine(r.Windows, @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache")],
                    FolderFallback: false),
            ]),
        ],
    };

    // ---- Tarayıcılar ve uygulamalar -----------------------------------------------------------------

    /// <summary>Chromium veri klasöründeki kullanıcı profillerinin (Default, Profile N) önbellek klasörleri.</summary>
    internal static IEnumerable<string> ChromiumCachePaths(string userDataPath)
    {
        if (!Directory.Exists(userDataPath)) yield break;
        foreach (var profile in Directory.EnumerateDirectories(userDataPath))
        {
            var name = Path.GetFileName(profile);
            if (name != "Default" && !name.StartsWith("Profile ", StringComparison.Ordinal)) continue;
            foreach (var cache in ChromiumCacheFolders) yield return Path.Combine(profile, cache);
        }
    }

    /// <summary>Firefox'un her profilindeki disk önbelleği (cache2). Yer imleri, geçmiş ve şifreler Roaming'deki profilde kalır.</summary>
    internal static IEnumerable<string> FirefoxCachePaths(string profilesPath)
    {
        if (!Directory.Exists(profilesPath)) yield break;
        foreach (var profile in Directory.EnumerateDirectories(profilesPath))
            yield return Path.Combine(profile, "cache2");
    }

    static CacheApp Chromium(string name, string process, string userData) =>
        App(name, [process], ChromiumCachePaths(userData).Select(p => new FolderSource(p)));

    // Opera profili, veri klasörünün kendisidir (Default alt klasörü yoktur).
    static CacheApp Opera(Roots r, string name, string folder) =>
        App(name, ["opera"],
            Folders(Path.Combine(r.Local, "Opera Software", folder), ChromiumCacheFolders)
                .Concat(Folders(Path.Combine(r.Roaming, "Opera Software", folder), ChromiumCacheFolders)));

    public static CacheTask Browsers(Roots r) => new()
    {
        Title = "Tarayıcı önbellekleri",
        Description = "Chrome, Edge, Brave, Vivaldi, Opera ve Firefox önbellekleri. Geçmiş, şifreler ve oturumlar silinmez. Açık tarayıcılar atlanır.",
        Group = TaskGroup.Apps,
        Apps = () =>
        [
            Chromium("Chrome", "chrome", Path.Combine(r.Local, @"Google\Chrome\User Data")),
            Chromium("Edge", "msedge", Path.Combine(r.Local, @"Microsoft\Edge\User Data")),
            Chromium("Brave", "brave", Path.Combine(r.Local, @"BraveSoftware\Brave-Browser\User Data")),
            Chromium("Vivaldi", "vivaldi", Path.Combine(r.Local, @"Vivaldi\User Data")),
            Opera(r, "Opera", "Opera Stable"),
            Opera(r, "Opera GX", "Opera GX Stable"),
            App("Firefox", ["firefox"], FirefoxCachePaths(Path.Combine(r.Local, @"Mozilla\Firefox\Profiles")).Select(p => new FolderSource(p))),
        ],
    };

    public static CacheTask AppCaches(Roots r) => new()
    {
        Title = "Uygulama önbellekleri",
        Description = "Discord, Slack ve VS Code'un önbellekleri. Oturumlar ve ayarlar silinmez. Açık uygulamalar atlanır.",
        Group = TaskGroup.Apps,
        Apps = () =>
        [
            App("Discord", ["Discord"], Folders(Path.Combine(r.Roaming, "discord"), ChromiumCacheFolders)),
            App("Slack", ["slack"], Folders(Path.Combine(r.Roaming, "Slack"), ChromiumCacheFolders)),
            App("VS Code", ["Code"], Folders(Path.Combine(r.Roaming, "Code"), [.. ChromiumCacheFolders, "CachedData", "CachedExtensionVSIXs"])),
        ],
    };

    public static CacheTask Spotify(Roots r) => new()
    {
        Title = "Spotify önbelleği",
        Description = "Dinlediğiniz şarkıların önbelleği; tek başına birkaç GB olabilir.",
        Group = TaskGroup.Apps,
        EnabledByDefault = false,
        Warning = "İndirdiğiniz (çevrimdışı) şarkılar da bu klasörde durabilir; silinirse yeniden indirmeniz gerekir.",
        Apps = () =>
        [
            App("Spotify", ["Spotify"],
                Folders(Path.Combine(r.Local, "Spotify"), "Storage", "Data")
                    .Concat(Folders(Path.Combine(r.Local, @"Packages\SpotifyAB.SpotifyMusic_zpdnekdrzrea0\LocalCache\Spotify"), "Data"))),
        ],
    };

    // ---- Geliştirici ------------------------------------------------------------------------------

    public static CacheTask DevCaches(Roots r) => new()
    {
        Title = "Paket yöneticisi önbellekleri",
        Description = "npm, Yarn, pnpm, pip ve NuGet indirme önbellekleri. Projelerinize dokunmaz; paketler gerekince tekrar indirilir.",
        Group = TaskGroup.Developer,
        Apps = () =>
        [
            // Sadece indirme önbelleği; derlemelerin kullandığı ~/.nuget/packages "Derleme paket depoları"nda.
            App("NuGet", [],
            [
                new CommandSource("dotnet.exe", "nuget locals http-cache --clear", [Path.Combine(r.Local, @"NuGet\v3-cache")]),
                new CommandSource("dotnet.exe", "nuget locals temp --clear", [Path.Combine(r.Temp, "NuGetScratch")]),
            ]),
            App("npm", [], [new CommandSource("npm.cmd", "cache clean --force",
                [Path.Combine(r.Local, @"npm-cache\_cacache"), Path.Combine(r.Roaming, @"npm-cache\_cacache")])]),
            App("Yarn", [], [new CommandSource("yarn.cmd", "cache clean", [Path.Combine(r.Local, @"Yarn\Cache")])]),
            // pnpm deposundaki dosyalar projelere bağlıdır; elle silinmez, sadece kullanılmayanları pnpm'in kendisi temizler.
            App("pnpm", [], [new CommandSource("pnpm.cmd|pnpm.exe", "store prune", [Path.Combine(r.Local, @"pnpm\store")], FolderFallback: false)]),
            App("pip", [], [new FolderSource(Path.Combine(r.Local, @"pip\cache"))]),
        ],
    };

    public static CacheTask PackageStores(Roots r) => new()
    {
        Title = "Derleme paket depoları",
        Description = "NuGet paketleri, Gradle, Cargo ve Go önbellekleri. Güvenle silinir ama sonraki derleme her şeyi yeniden indirir.",
        Group = TaskGroup.Developer,
        EnabledByDefault = false,
        Apps = () =>
        [
            App("NuGet paketleri", [], [new CommandSource("dotnet.exe", "nuget locals global-packages --clear",
                [Path.Combine(r.Home, @".nuget\packages")])]),
            App("Gradle", [], [new FolderSource(Path.Combine(r.Home, @".gradle\caches"))]),
            // ~/.cargo/bin kurulu araçları içerir; sadece indirme ve kaynak önbellekleri temizlenir.
            App("Cargo", [], Folders(Path.Combine(r.Home, ".cargo"), @"registry\cache", @"registry\src", @"git\checkouts")),
            App("Go", [], [new CommandSource("go.exe", "clean -cache", [Path.Combine(r.Local, "go-build")])]),
        ],
    };
}
