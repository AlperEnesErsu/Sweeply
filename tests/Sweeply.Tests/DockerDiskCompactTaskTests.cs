using Sweeply.Tasks;

namespace Sweeply.Tests;

public class DockerDiskCompactTaskTests
{
    /// <summary>Çalıştırılan komutları kaydeden sahte çalıştırıcı; diskpart çağrısında diski küçültebilir.</summary>
    sealed class FakeRunner
    {
        public readonly List<(string File, string Args, string? Input)> Calls = new();
        public Func<string, string?, int> Exit { get; set; } = (_, _) => 0;
        public Action<string?>? OnDiskpart { get; set; }

        public Task<(int, string)> Run(string file, string args, TimeSpan timeout, CancellationToken ct, string? input)
        {
            Calls.Add((file, args, input));
            if (file == "diskpart.exe") OnDiskpart?.Invoke(input);
            return Task.FromResult((Exit(file, input), "DiskPart çıktısı\nVirtual Disk Service error: dosya kullanımda"));
        }
    }

    static DockerDiskCompactTask Make(TempDir dir, FakeRunner runner, bool admin = true, Func<bool>? dockerRunning = null) => new()
    {
        Disks = () => DockerDiskCompactTask.DefaultDisks(dir.Path),
        IsAdmin = () => admin,
        IsDockerRunning = dockerRunning ?? (() => false),
        Run = runner.Run,
        FindDocker = () => @"C:\docker\docker.exe",
        StopWait = TimeSpan.Zero,
    };

    static string Disk(TempDir dir, int bytes = 4096) => dir.File(@"Docker\wsl\disk\docker_data.vhdx", bytes);

    [Fact]
    public void Defaults_AreOffAndRequireAdmin()
    {
        var task = new DockerDiskCompactTask();
        Assert.False(task.EnabledByDefault);
        Assert.True(task.RequiresAdmin);
        Assert.True(((ICleanupTask)task).RequiresAdmin);
    }

    [Fact]
    public void CompactScript_AttachesReadOnlyAndDetaches()
    {
        var script = DockerDiskCompactTask.CompactScript(@"C:\Users\x\AppData\Local\Docker\wsl\disk\docker_data.vhdx");
        var lines = script.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(
        [
            @"select vdisk file=""C:\Users\x\AppData\Local\Docker\wsl\disk\docker_data.vhdx""",
            "attach vdisk readonly",
            "compact vdisk",
            "detach vdisk",
            "exit",
        ], lines);
    }

    [Theory]
    [InlineData(@"C:\Users\alper\AppData\Local\Docker\wsl\disk\docker_data.vhdx", true)]
    [InlineData(@"C:\Users\Ömer\AppData\Local\Docker\wsl\disk\docker_data.vhdx", false)]
    [InlineData("C:\\a\"\r\ndelete\\x.vhdx", false)]
    public void SafePath_OnlyAllowsPlainAscii(string path, bool safe) =>
        Assert.Equal(safe, DockerDiskCompactTask.SafePath(path));

    [Fact]
    public async Task Analyze_NoDisk_Unavailable()
    {
        using var dir = new TempDir();
        var analysis = await Make(dir, new FakeRunner()).AnalyzeAsync(CancellationToken.None);
        Assert.False(analysis.Available);
        Assert.Equal(0, analysis.Bytes);
    }

    [Fact]
    public async Task Analyze_NotAdmin_UnavailableWithNote()
    {
        using var dir = new TempDir();
        Disk(dir);
        var analysis = await Make(dir, new FakeRunner(), admin: false).AnalyzeAsync(CancellationToken.None);
        Assert.False(analysis.Available);
        Assert.Contains("Yönetici", analysis.Note);
    }

    [Fact]
    public async Task Analyze_Admin_AvailableWithUnknownGain()
    {
        using var dir = new TempDir();
        Disk(dir);
        var analysis = await Make(dir, new FakeRunner()).AnalyzeAsync(CancellationToken.None);
        Assert.True(analysis.Available);
        Assert.Equal(-1, analysis.Bytes);
    }

    [Fact]
    public async Task Preview_ListsCommandAndDeletesNothing()
    {
        using var dir = new TempDir();
        var disk = Disk(dir);
        var runner = new FakeRunner();

        var preview = await Make(dir, runner).PreviewAsync(CancellationToken.None);

        var entry = Assert.Single(preview.Entries);
        Assert.True(entry.IsCommand);
        Assert.Contains(disk, entry.Label);
        Assert.Empty(runner.Calls);
        Assert.True(File.Exists(disk));
    }

    [Fact]
    public async Task Run_NotAdmin_RunsNothing()
    {
        using var dir = new TempDir();
        Disk(dir);
        var runner = new FakeRunner();

        var result = await Make(dir, runner, admin: false).RunAsync(null, CancellationToken.None);

        Assert.Empty(runner.Calls);
        Assert.Equal(0, result.FreedBytes);
    }

    [Fact]
    public async Task Run_DockerWontStop_DoesNotTouchDisk()
    {
        using var dir = new TempDir();
        Disk(dir);
        var runner = new FakeRunner();

        var result = await Make(dir, runner, dockerRunning: () => true).RunAsync(null, CancellationToken.None);

        Assert.Equal([@"C:\docker\docker.exe"], runner.Calls.Select(c => c.File));
        Assert.Contains("Docker Desktop kapanmadı", result.Message);
    }

    [Fact]
    public async Task Run_WslShutdownFails_DoesNotTouchDisk()
    {
        using var dir = new TempDir();
        Disk(dir);
        var runner = new FakeRunner { Exit = (file, _) => file == "wsl.exe" ? 1 : 0 };

        await Make(dir, runner).RunAsync(null, CancellationToken.None);

        Assert.DoesNotContain(runner.Calls, c => c.File == "diskpart.exe");
    }

    [Fact]
    public async Task Run_Success_StopsDockerShutsDownWslThenCompactsViaStdin()
    {
        using var dir = new TempDir();
        var disk = Disk(dir, 4096);
        bool running = true;
        // diskpart diski 4096 bayttan 1024 bayta küçültmüş gibi davranır.
        var runner = new FakeRunner { OnDiskpart = _ => File.WriteAllBytes(disk, new byte[1024]) };
        var task = Make(dir, runner, dockerRunning: () => running) with
        {
            // "desktop stop" çağrıldıktan sonra Docker kapanmış sayılır.
            Run = (f, a, t, c, i) => { if (a == "desktop stop") running = false; return runner.Run(f, a, t, c, i); },
        };

        var result = await task.RunAsync(null, CancellationToken.None);

        Assert.Equal([@"C:\docker\docker.exe", "wsl.exe", "diskpart.exe"], runner.Calls.Select(c => c.File));
        var diskpart = runner.Calls[2];
        Assert.Equal("", diskpart.Args);  // betik dosyası yok, komutlar standart girdiden
        Assert.Equal(DockerDiskCompactTask.CompactScript(disk), diskpart.Input);
        Assert.Equal(3072, result.FreedBytes);
        Assert.Contains("geri verildi", result.Message);
    }

    [Fact]
    public async Task Run_DiskpartFails_TriesToDetachAndReportsError()
    {
        using var dir = new TempDir();
        var disk = Disk(dir);
        var runner = new FakeRunner { Exit = (file, input) => file == "diskpart.exe" && input!.Contains("compact") ? 5 : 0 };

        var result = await Make(dir, runner).RunAsync(null, CancellationToken.None);

        var diskparts = runner.Calls.Where(c => c.File == "diskpart.exe").ToList();
        Assert.Equal(2, diskparts.Count);
        Assert.Equal(DockerDiskCompactTask.DetachScript(disk), diskparts[1].Input);
        Assert.Contains("Küçültülemedi", result.Message);
        Assert.Contains("dosya kullanımda", result.Message);
        Assert.Equal(0, result.FreedBytes);
    }

    [Fact]
    public async Task Run_CancelledBeforeCompact_SkipsDiskpart()
    {
        using var dir = new TempDir();
        Disk(dir);
        var runner = new FakeRunner();
        using var cts = new CancellationTokenSource();
        var task = new DockerDiskCompactTask
        {
            Disks = () => DockerDiskCompactTask.DefaultDisks(dir.Path),
            IsAdmin = () => true,
            IsDockerRunning = () => false,
            FindDocker = () => null,
            Run = (f, a, t, c, i) => { if (f == "wsl.exe") cts.Cancel(); return runner.Run(f, a, t, c, i); },
        };

        var result = await task.RunAsync(null, cts.Token);

        Assert.DoesNotContain(runner.Calls, c => c.File == "diskpart.exe");
        Assert.Equal("Durduruldu", result.Message);
    }

    [Fact]
    public void Catalog_IncludesTaskAfterBuildCache()
    {
        var titles = Catalog.All().Select(t => t.Title).ToList();
        int prune = titles.IndexOf("Docker build cache");
        int compact = titles.IndexOf("Docker sanal diskini küçült");
        Assert.True(prune >= 0 && compact == prune + 1);
    }
}
