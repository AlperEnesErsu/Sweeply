using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Sweeply.Controls;
using Sweeply.Models;
using Sweeply.Services;
using Sweeply.Tasks;

namespace Sweeply;

public partial class MainWindow : Window
{
    readonly ObservableCollection<CleanupItem> _items = new();
    readonly ObservableCollection<ProcessGroup> _processes = new();
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3) };
    readonly bool _autoRun;
    bool _busy;
    bool _showingResult;
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
        foreach (var job in jobs)
        {
            var item = new CleanupItem(job);
            item.PropertyChanged += OnItemChanged;
            _items.Add(item);
        }

        TasksList.ItemsSource = _items;
        ProcList.ItemsSource = _processes;
        UpdateSelection();

        _timer.Tick += async (_, _) =>
        {
            UpdateHealth();
            if (++_tick % 2 == 0) await RefreshProcessesAsync();
        };
        SourceInitialized += (_, _) =>
            WindowTheme.ApplyDark(this, (Color)FindResource("BgColor"), (Color)FindResource("LineColor"));
        Loaded += OnLoaded;
    }

    Brush Res(string key) => (Brush)FindResource(key);

    async void OnLoaded(object sender, RoutedEventArgs e)
    {
        PlayEntrance();
        UpdateHealth();
        await RefreshProcessesAsync();
        _timer.Start();

        await AnalyzeAsync();
        if (_autoRun) await SweepAsync();
    }

    /// <summary>Sadece açılışta: ölçümler ve görev satırları kısa aralıklarla sırayla belirir.</summary>
    void PlayEntrance()
    {
        Motion.FadeUp(Hero);
        int i = 0;
        foreach (UIElement cell in Strip.Children) Motion.FadeUp(cell, 60 + i++ * 40);

        Dispatcher.InvokeAsync(() =>
        {
            for (int row = 0; row < _items.Count; row++)
            {
                if (TasksList.ItemContainerGenerator.ContainerFromIndex(row) is UIElement container)
                    Motion.FadeUp(container, 180 + row * 40);
            }
        }, DispatcherPriority.Loaded);
    }

    void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CleanupItem.IsEnabled) or nameof(CleanupItem.IsAvailable) or nameof(CleanupItem.Bytes))
            UpdateSelection();
    }

    /// <summary>Seçim değişince buton durumu, seçili sayısı ve (sonuç gösterilmiyorsa) başlıktaki tahmin güncellenir.</summary>
    void UpdateSelection()
    {
        var selected = _items.Where(i => i.IsEnabled && i.IsAvailable).ToList();
        SelectedCount.Text = $"{selected.Count} / {_items.Count} seçili";
        SweepButton.IsEnabled = !_busy && selected.Count > 0;

        if (_busy || _showingResult) return;
        long disk = selected.Where(i => i.Job.Kind == TaskKind.Disk).Sum(i => Math.Max(0, i.Bytes));
        HeadlineValue.Text = Format.Bytes(disk);
        HeadlineSub.Text = selected.Count == 0
            ? "Temizlemek için en az bir görev seçin."
            : $"{selected.Count} görev seçili · Temp klasöründe sadece 24 saatten eski dosyalar silinir.";
    }

    void UpdateHealth()
    {
        var s = SystemInfo.GetSnapshot();

        RamText.Text = $"{Format.Bytes(s.RamUsed)} / {Format.Bytes(s.RamTotal)}";
        RamBar.Value = s.RamLoad;
        ShowNote(RamBar, RamSub, HealthTips.Ram(s), $"%{s.RamLoad} dolu · {Format.Bytes(s.RamAvailable)} boş");

        DiskLabel.Text = $"Disk {s.DiskName}";
        DiskText.Text = $"{Format.Bytes(s.DiskFree)} boş";
        DiskBar.Value = s.DiskUsedPercent;
        ShowNote(DiskBar, DiskSub, HealthTips.Disk(s), $"{Format.Bytes(s.DiskTotal)} kapasite");

        UptimeText.Text = Format.Duration(s.Uptime);
        ShowNote(null, UptimeSub, HealthTips.Uptime(s), "son açılıştan beri");

        StartupText.Text = s.StartupCount.ToString();
        ShowNote(null, StartupSub, HealthTips.Startup(s), "açılışta çalışan program");
    }

    /// <summary>Ölçümün alt satırı: sorun varsa sarı uyarı (çubuk da sarı), yoksa sakin bir açıklama.</summary>
    void ShowNote(ProgressBar? bar, TextBlock line, string? warning, string normal)
    {
        line.Text = warning ?? normal;
        line.Foreground = Res(warning != null ? "Warn" : "Muted");
        if (bar != null) bar.Foreground = Res(warning != null ? "Warn" : "Text");
    }

    async Task RefreshProcessesAsync()
    {
        var top = await Task.Run(() => ProcessService.GetTop(12));
        _processes.Clear();
        foreach (var p in top) _processes.Add(p);
    }

    async Task AnalyzeAsync()
    {
        await Task.WhenAll(_items.Select(AnalyzeItemAsync));
        UpdateSelection();
    }

    static async Task AnalyzeItemAsync(CleanupItem item)
    {
        item.Estimate = "…";
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

    async Task SweepAsync()
    {
        if (_busy) return;
        var queue = _items.Where(i => i.IsEnabled && i.IsAvailable).ToList();
        if (queue.Count == 0) return;

        SetBusy(true);
        foreach (var item in _items)
        {
            item.State = ItemState.Idle;
            item.Status = null;
        }

        HeadlineCaption.Text = "Temizleniyor";
        RunProgress.Value = 0;
        RunProgress.Visibility = Visibility.Visible;
        long disk = 0, ram = 0;

        for (int i = 0; i < queue.Count; i++)
        {
            var item = queue[i];
            item.State = ItemState.Running;
            item.Status = "temizleniyor…";
            HeadlineSub.Text = $"{item.Title} ({i + 1}/{queue.Count})";
            try
            {
                var result = await item.Job.RunAsync(CancellationToken.None);
                item.Status = result.Message;
                item.State = ItemState.Done;
                if (item.Job.Kind == TaskKind.Ram) ram += result.FreedBytes;
                else disk += result.FreedBytes;
            }
            catch (Exception ex)
            {
                item.Status = ex.Message;
                item.State = ItemState.Failed;
            }
            RunProgress.Value = 100.0 * (i + 1) / queue.Count;
        }

        RunProgress.Visibility = Visibility.Collapsed;
        _showingResult = true;
        await ShowResultAsync(disk, ram);

        UpdateHealth();
        await RefreshProcessesAsync();
        SetBusy(false);
        SweepLabel.Text = "Tekrar temizle";
        await AnalyzeAsync();
    }

    async Task ShowResultAsync(long disk, long ram)
    {
        if (disk == 0 && ram == 0)
        {
            HeadlineCaption.Text = "Sonuç";
            HeadlineValue.Text = "Zaten temiz";
            HeadlineSub.Text = "Silinebilecek bir şey bulunamadı. Ayrıntılar her görevin altında.";
            return;
        }

        HeadlineCaption.Text = "Temizlendi";
        HeadlineValue.Foreground = Res("Good");
        HeadlineSub.Text = ram > 0
            ? $"disk alanı açıldı · ayrıca {Format.Bytes(ram)} RAM boşaldı"
            : "disk alanı açıldı · ayrıntılar her görevin altında";
        await Motion.CountUpAsync(HeadlineValue, disk, Format.Bytes);
    }

    void SetBusy(bool busy)
    {
        _busy = busy;
        RefreshButton.IsEnabled = !busy;
        if (busy) SweepLabel.Text = "Temizleniyor…";
        UpdateSelection();
    }

    async void SweepButton_Click(object sender, RoutedEventArgs e) => await SweepAsync();

    async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _items)
        {
            item.State = ItemState.Idle;
            item.Status = null;
        }
        _showingResult = false;
        HeadlineCaption.Text = "Temizlenebilir alan";
        HeadlineValue.Foreground = Res("Text");
        HeadlineValue.Text = "…";
        SweepLabel.Text = "Temizle";
        UpdateHealth();
        await RefreshProcessesAsync();
        await AnalyzeAsync();
    }

    void ShortcutButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ShortcutService.CreateDesktopShortcut();
            Dialog.Show(this, "Kısayol oluşturuldu",
                "Masaüstündeki Sweeply simgesine çift tıkladığınızda uygulama açılır ve seçili görevleri otomatik çalıştırır.");
        }
        catch (Exception ex)
        {
            Dialog.Show(this, "Kısayol oluşturulamadı", ex.Message, DialogKind.Error);
        }
    }

    async void CloseProcess_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ProcessGroup group) return;

        if (!Dialog.Confirm(this, $"{group.Name} kapatılsın mı?",
                $"{group.CountText}, {group.MemoryText} RAM kullanıyor. Kaydedilmemiş işleriniz varsa önce kaydedin.",
                "Kapat"))
            return;

        int remaining = await ProcessService.CloseGracefullyAsync(group.Name);
        if (remaining > 0 && Dialog.Confirm(this, $"{group.Name} hâlâ açık",
                $"{remaining} işlem kapanmadı. Zorla kapatırsanız kaydedilmemiş veriler kaybolabilir.",
                "Zorla kapat", kind: DialogKind.Warning))
        {
            ProcessService.Kill(group.Name);
        }

        await Task.Delay(500);
        UpdateHealth();
        await RefreshProcessesAsync();
    }
}
