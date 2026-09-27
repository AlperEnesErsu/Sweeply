using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Optimayzir.Models;
using Optimayzir.Services;
using Optimayzir.Tasks;

namespace Optimayzir;

public partial class MainWindow : Window
{
    readonly ObservableCollection<CleanupItem> _items = new();
    readonly ObservableCollection<ProcessGroup> _processes = new();
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3) };
    readonly bool _autoRun;
    bool _busy;
    int _tick;

    public MainWindow(bool autoRun)
    {
        InitializeComponent();
        _autoRun = autoRun;

        ICleanupTask[] jobs =
        [
            new TempFilesTask(),
            new BrowserCacheTask(),
            new DevCachesTask(),
            new CrashDumpsTask(),
            new DockerBuildCacheTask(),
            new WslShutdownTask(),
        ];
        foreach (var job in jobs) _items.Add(new CleanupItem(job));

        TasksList.ItemsSource = _items;
        ProcList.ItemsSource = _processes;

        _timer.Tick += async (_, _) =>
        {
            UpdateHealth();
            if (++_tick % 2 == 0) await RefreshProcessesAsync();
        };
        Loaded += OnLoaded;
    }

    async void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateHealth();
        await RefreshProcessesAsync();
        _timer.Start();

        await AnalyzeAsync();
        if (_autoRun) await OptimizeAsync();
    }

    void UpdateHealth()
    {
        var s = SystemInfo.GetSnapshot();
        var warn = (Brush)FindResource("Warn");
        var accent = (Brush)FindResource("Accent");

        RamText.Text = $"{Format.Bytes(s.RamUsed)} / {Format.Bytes(s.RamTotal)}";
        RamBar.Value = s.RamLoad;
        RamBar.Foreground = s.RamLoad >= 85 ? warn : accent;
        RamSub.Text = $"%{s.RamLoad} dolu · {Format.Bytes(s.RamAvailable)} boş";

        DiskText.Text = $"{Format.Bytes(s.DiskFree)} boş";
        DiskBar.Value = s.DiskUsedPercent;
        DiskBar.Foreground = s.DiskUsedPercent >= 85 ? warn : accent;
        DiskSub.Text = $"{s.DiskName} · toplam {Format.Bytes(s.DiskTotal)}";

        UptimeText.Text = Format.Duration(s.Uptime);
        StartupText.Text = s.StartupCount.ToString();

        var tips = new List<string>();
        if (s.DiskUsedPercent >= 85)
            tips.Add($"Disk %{s.DiskUsedPercent:0} dolu. SSD'ler dolmaya yaklaştıkça yavaşlar; en az %15 boş alan bırakmaya çalışın.");
        if (s.RamLoad >= 85)
            tips.Add("RAM neredeyse dolu. Sağdaki listeden kullanmadığınız programları kapatın.");
        if (s.Uptime.TotalDays >= 3)
            tips.Add($"Bilgisayar {(int)s.Uptime.TotalDays} gündür yeniden başlatılmadı. Yeniden başlatmak biriken yükü temizler.");
        if (s.StartupCount >= 8)
            tips.Add("Açılışta çok program çalışıyor. Görev Yöneticisi → Başlangıç uygulamaları sekmesinden gereksizleri kapatın.");
        TipsList.ItemsSource = tips;
        TipsBorder.Visibility = tips.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    async Task RefreshProcessesAsync()
    {
        var top = await Task.Run(() => ProcessService.GetTop(12));
        _processes.Clear();
        foreach (var p in top) _processes.Add(p);
    }

    async Task AnalyzeAsync()
    {
        StatusText.Text = "Sistem analiz ediliyor…";
        await Task.WhenAll(_items.Select(AnalyzeItemAsync));

        long disk = _items.Where(i => i.IsAvailable && i.Job.Kind == TaskKind.Disk).Sum(i => Math.Max(0, i.Bytes));
        StatusText.Text = $"Analiz tamamlandı · yaklaşık {Format.Bytes(disk)} disk alanı kazanılabilir.";
    }

    static async Task AnalyzeItemAsync(CleanupItem item)
    {
        item.Estimate = "hesaplanıyor…";
        Analysis analysis;
        try
        {
            analysis = await item.Job.AnalyzeAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            analysis = new Analysis(false, 0, ex.Message);
        }

        item.IsAvailable = analysis.Available;
        item.Bytes = analysis.Bytes;
        item.Note = analysis.Note;
        item.Estimate = !analysis.Available ? "—" : analysis.Bytes < 0 ? "?" : Format.Bytes(analysis.Bytes);
    }

    async Task OptimizeAsync()
    {
        if (_busy) return;
        SetBusy(true);
        SummaryText.Text = "";

        long disk = 0, ram = 0;
        foreach (var item in _items.Where(i => i.IsEnabled && i.IsAvailable).ToList())
        {
            item.Status = "çalışıyor…";
            StatusText.Text = $"{item.Title} temizleniyor…";
            try
            {
                var result = await item.Job.RunAsync(CancellationToken.None);
                item.Status = "✓ " + result.Message;
                if (item.Job.Kind == TaskKind.Ram) ram += result.FreedBytes;
                else disk += result.FreedBytes;
            }
            catch (Exception ex)
            {
                item.Status = "✗ " + ex.Message;
            }
        }

        SummaryText.Text = $"✓ Tamamlandı: {Format.Bytes(disk)} disk alanı" + (ram > 0 ? $" ve {Format.Bytes(ram)} RAM" : "") + " kazanıldı.";
        UpdateHealth();
        await RefreshProcessesAsync();
        await AnalyzeAsync();
        SetBusy(false);
        OptimizeButton.Content = "⚡  Tekrar Optimize Et";
    }

    void SetBusy(bool busy)
    {
        _busy = busy;
        OptimizeButton.IsEnabled = !busy;
        RefreshButton.IsEnabled = !busy;
        if (busy) OptimizeButton.Content = "Optimize ediliyor…";
    }

    async void OptimizeButton_Click(object sender, RoutedEventArgs e) => await OptimizeAsync();

    async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _items) item.Status = null;
        SummaryText.Text = "";
        UpdateHealth();
        await RefreshProcessesAsync();
        await AnalyzeAsync();
    }

    void ShortcutButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ShortcutService.CreateDesktopShortcut();
            MessageBox.Show(this, "Masaüstüne \"Optimayzır\" kısayolu eklendi.\nÇift tıkladığınızda uygulama açılır ve otomatik olarak optimize eder.",
                "Kısayol oluşturuldu", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Kısayol oluşturulamadı: " + ex.Message, "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    async void CloseProcess_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ProcessGroup group) return;

        var confirm = MessageBox.Show(this,
            $"{group.Name} ({group.CountText}, {group.MemoryText}) kapatılsın mı?\nKaydedilmemiş işleriniz varsa önce kaydedin.",
            "Programı kapat", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        StatusText.Text = $"{group.Name} kapatılıyor…";
        int remaining = await ProcessService.CloseGracefullyAsync(group.Name);
        if (remaining > 0)
        {
            var force = MessageBox.Show(this,
                $"{group.Name} hâlâ açık ({remaining} işlem). Zorla kapatılsın mı?\nKaydedilmemiş veriler kaybolabilir.",
                "Zorla kapat", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (force == MessageBoxResult.Yes) ProcessService.Kill(group.Name);
        }

        await Task.Delay(500);
        UpdateHealth();
        await RefreshProcessesAsync();
        StatusText.Text = $"{group.Name} kapatıldı.";
    }
}
