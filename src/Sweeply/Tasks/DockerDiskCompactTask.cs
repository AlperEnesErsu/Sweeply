using Sweeply.Services;

namespace Sweeply.Tasks;

/// <summary>
/// Docker Desktop'ın sanal diskini (<c>docker_data.vhdx</c>) küçültür. Docker içinde silinen veriler (ör. build cache)
/// bu dosyada boş alan olarak kalır; <c>diskpart compact vdisk</c> bu alanı Windows'a geri verir.
/// Disk salt okunur bağlanır, Docker verilerine dokunulmaz. Yönetici izni gerekir ve Docker Desktop ile WSL kapatılır;
/// bu yüzden varsayılan olarak kapalıdır ve zamanlanmış temizlikte hiç çalışmaz.
/// </summary>
public sealed record DockerDiskCompactTask : ICleanupTask
{
    public string Title => "Docker sanal diskini küçült";
    public string Description => "Docker içinde boşalan alanı Windows'a geri verir. Docker Desktop ve WSL kapatılır; veriler silinmez. Yönetici izni gerekir.";
    public TaskKind Kind => TaskKind.Disk;
    public TaskGroup Group => TaskGroup.Developer;
    public bool EnabledByDefault => false;
    public bool RequiresAdmin => true;

    /// <summary>Docker Desktop'ın kullandığı sanal disk konumları (yeni ve eski sürümler).</summary>
    public static string[] DefaultDisks(string local) =>
    [
        Path.Combine(local, @"Docker\wsl\disk\docker_data.vhdx"),
        Path.Combine(local, @"Docker\wsl\data\ext4.vhdx"),
    ];

    /// <summary>Docker Desktop'ın çalıştığını gösteren işlemler; biri açıksa disk kullanımdadır.</summary>
    internal static readonly string[] DockerProcesses = ["Docker Desktop", "com.docker.backend", "com.docker.build"];

    // Testlerde değiştirilebilen bağımlılıklar.
    public Func<IEnumerable<string>> Disks { get; init; } =
        () => DefaultDisks(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
    public Func<bool> IsAdmin { get; init; } = () => Elevation.IsAdmin;
    public Func<bool> IsDockerRunning { get; init; } = () => DockerProcesses.Any(p => CommandRunner.CountProcesses(p) > 0);
    /// <summary>Komut çalıştırıcı: dosya, argümanlar, zaman aşımı, iptal, standart girdi.</summary>
    public Func<string, string, TimeSpan, CancellationToken, string?, Task<(int ExitCode, string Output)>> Run { get; init; } = CommandRunner.RunAsync;
    public Func<string?> FindDocker { get; init; } = () => CommandRunner.FindOnPath("docker.exe");
    public TimeSpan StopWait { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>Küçültme uzun sürebilir (büyük disklerde dakikalar). Yarıda kesilirse disk bağlı kalabileceği için iptal edilmez.</summary>
    public static readonly TimeSpan CompactTimeout = TimeSpan.FromMinutes(60);

    List<string> Existing() => Disks().Where(File.Exists).ToList();

    /// <summary>
    /// diskpart komutları konsol kod sayfasında okur; yolda ASCII dışı harf (ör. "Ö" içeren kullanıcı adı) varsa
    /// yanlış dosya adı okunabilir. Bu durumda görev hiç çalışmaz.
    /// </summary>
    internal static bool SafePath(string path) => path.All(c => c is >= ' ' and <= '~' && c != '"');

    const string UnsafePathNote = "Disk yolunda Türkçe ya da özel karakter var; diskpart bu yolu güvenle okuyamaz. README'deki elle küçültme adımlarını kullanın.";

    static long Size(string file)
    {
        try { return new FileInfo(file).Length; }
        catch { return 0; }
    }

    /// <summary>diskpart betiği: diski salt okunur bağlar, küçültür ve ayırır.</summary>
    internal static string CompactScript(string vhdx) =>
        $"select vdisk file=\"{vhdx}\"\r\nattach vdisk readonly\r\ncompact vdisk\r\ndetach vdisk\r\nexit\r\n";

    /// <summary>Küçültme hata verirse disk bağlı kalmasın diye çalıştırılan betik.</summary>
    internal static string DetachScript(string vhdx) =>
        $"select vdisk file=\"{vhdx}\"\r\ndetach vdisk\r\nexit\r\n";

    public Task<Analysis> AnalyzeAsync(CancellationToken ct) => Task.Run(() =>
    {
        var disks = Existing();
        if (disks.Count == 0) return new Analysis(false, 0, "Bu bilgisayarda bulunamadı.");

        string size = $"Sanal disk şu an {Format.Bytes(disks.Sum(Size))}.";
        if (!disks.All(SafePath)) return new Analysis(false, -1, $"{size} {UnsafePathNote}");
        if (!IsAdmin())
            return new Analysis(false, -1, $"{size} Yönetici izni gerekir; üstteki \"Yönetici olarak aç\" ile yeniden başlatın.");
        // Kazanç, sanal diskin içindeki boş alana bağlıdır; önceden bilinemez.
        return new Analysis(true, -1, $"{size} Docker Desktop ve WSL kapatılır; çalışan konteynerler durur.");
    }, ct);

    public Task<Preview> PreviewAsync(CancellationToken ct) => Task.Run(() =>
    {
        var entries = Existing()
            .Select(d => new PreviewEntry($"diskpart compact vdisk \"{d}\" (şu an {Format.Bytes(Size(d))})", 0, 0, IsCommand: true))
            .ToList();
        return new Preview(entries,
            "Dosya silinmez. Docker Desktop ve WSL kapatılır, sanal disk salt okunur bağlanıp küçültülür. Kazanç önceden bilinemez.");
    }, ct);

    public async Task<TaskResult> RunAsync(IProgress<CleanProgress>? progress, CancellationToken ct)
    {
        if (!IsAdmin()) return new TaskResult(0, "Yönetici izni gerekir");
        var disks = Existing();
        if (disks.Count == 0) return new TaskResult(0, "Docker sanal diski bulunamadı");
        if (!disks.All(SafePath)) return new TaskResult(0, UnsafePathNote);

        // Disk kullanımdayken küçültülemez: önce Docker Desktop, sonra WSL kapatılır.
        if (IsDockerRunning())
        {
            var docker = FindDocker();
            if (docker != null) await Run(docker, "desktop stop", TimeSpan.FromSeconds(90), ct, null);
            await Task.Delay(StopWait, ct);
            if (IsDockerRunning())
                return new TaskResult(0, "Docker Desktop kapanmadı; ondan çıkıp (Quit Docker Desktop) tekrar deneyin");
        }

        var shutdown = await Run("wsl.exe", "--shutdown", TimeSpan.FromSeconds(60), ct, null);
        if (shutdown.ExitCode != 0) return new TaskResult(0, "WSL kapatılamadı; sanal disk kullanımda olabilir");

        long freed = 0;
        var failures = new List<string>();
        foreach (var disk in disks)
        {
            // Bir disk küçültülürken iptal beklenmez; sıradakine geçmeden önce bakılır.
            if (ct.IsCancellationRequested) break;

            long before = Size(disk);
            var error = await CompactAsync(disk);
            if (error != null) failures.Add(error);
            freed += Math.Max(0, before - Size(disk));
            progress?.Report(new CleanProgress(0, freed));
        }

        if (failures.Count > 0) return new TaskResult(freed, "Küçültülemedi: " + string.Join("; ", failures));
        if (ct.IsCancellationRequested) return new TaskResult(freed, "Durduruldu");
        return new TaskResult(freed, freed > 0
            ? $"{Format.Bytes(freed)} Windows'a geri verildi. Docker Desktop'ı yeniden açabilirsiniz."
            : "Sanal diskte geri verilecek boş alan yoktu");
    }

    /// <summary>
    /// Komutlar diskpart'a standart girdiden verilir. Betik dosyası kullanılmaz: yönetici olarak çalışan diskpart'ın
    /// okuyacağı bir dosya, kullanıcının yazabildiği Temp klasöründe arada değiştirilebilirdi.
    /// </summary>
    /// <returns>Başarılıysa null, aksi halde kısa hata açıklaması.</returns>
    async Task<string?> CompactAsync(string disk)
    {
        try
        {
            var (exit, output) = await Run("diskpart.exe", "", CompactTimeout, CancellationToken.None, CompactScript(disk));
            if (exit == 0) return null;

            await Run("diskpart.exe", "", TimeSpan.FromMinutes(2), CancellationToken.None, DetachScript(disk));
            var last = output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            return $"{Path.GetFileName(disk)}: {last ?? "diskpart hata verdi"}";
        }
        catch (Exception ex)
        {
            return $"{Path.GetFileName(disk)}: {ex.Message}";
        }
    }
}
