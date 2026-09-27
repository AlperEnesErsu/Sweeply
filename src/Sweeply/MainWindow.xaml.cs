using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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
    CancellationTokenSource? _cts;
    bool _busy;
    bool _showingResult;
    int _tick;

    public MainWindow(bool autoRun)
    {
        InitializeComponent();
        _autoRun = autoRun;

        foreach (var job in Catalog.All())
        {
            var item = new CleanupItem(job);
            item.PropertyChanged += OnItemChanged;
            _items.Add(item);
        }

        // Görevler katalogda zaten grup sırasıyla dizili; başlıklar ilk görünüş sırasını izler.
        var view = CollectionViewSource.GetDefaultView(_items);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(CleanupItem.GroupName)));
        TasksList.ItemsSource = view;
        ProcList.ItemsSource = _processes;

        if (Elevation.IsAdmin)
        {
            AdminButton.Visibility = Visibility.Collapsed;
            Title = "Sweeply (Yönetici)";
        }
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

    /// <summary>Sadece açılışta: ölçümler ve görev grupları kısa aralıklarla sırayla belirir.</summary>
    void PlayEntrance()
    {
        Motion.FadeUp(Hero);
        int i = 0;
        foreach (UIElement cell in Strip.Children) Motion.FadeUp(cell, 60 + i++ * 40);

        Dispatcher.InvokeAsync(() =>
        {
            for (int group = 0; TasksList.ItemContainerGenerator.ContainerFromIndex(group) is UIElement container; group++)
                Motion.FadeUp(container, 180 + group * 60);
        }, DispatcherPriority.Loaded);
    }

    List<CleanupItem> Selected() => _items.Where(i => i.IsEnabled && i.IsAvailable).ToList();

    void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CleanupItem.IsEnabled) or nameof(CleanupItem.IsAvailable) or nameof(CleanupItem.Bytes))
            UpdateSelection();
    }

    /// <summary>Seçim değişince butonlar, seçili sayısı ve (sonuç gösterilmiyorsa) başlıktaki tahmin güncellenir.</summary>
    void UpdateSelection()
    {
        var selected = Selected();
        SelectedCount.Text = $"{selected.Count} / {_items.Count} seçili";
        SweepButton.IsEnabled = _busy || selected.Count > 0;  // çalışırken "Durdur" olarak kullanılır
        PreviewButton.IsEnabled = !_busy && selected.Count > 0;

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
        ShowNote(null, UptimeSub, HealthTips.Uptime(s), s.FastStartup ? "Hızlı Başlangıç açık" : "açılış geçmişi için tıklayın");

        StartupText.Text = s.StartupCount.ToString();
        ShowNote(null, StartupSub, HealthTips.Startup(s), "listeyi görmek için tıklayın");
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
        // Yönetici izni bekleyen görevlerde de boyut gösterilir: kullanıcı neyi kaçırdığını görsün.
        item.Estimate = analysis.Bytes < 0 ? "?"
            : !analysis.Available && analysis.Bytes == 0 ? "—"
            : Format.Bytes(analysis.Bytes);
    }

    async Task SweepAsync()
    {
        if (_busy) return;
        var queue = Selected();
        if (queue.Count == 0) return;

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        SetBusy(true);
        foreach (var item in _items)
        {
            item.State = ItemState.Idle;
            item.Status = null;
        }

        HeadlineCaption.Text = "Temizleniyor";
        HeadlineValue.Foreground = Res("Text");
        RunProgress.Value = 0;
        RunProgress.Visibility = Visibility.Visible;
        long disk = 0, ram = 0;

        for (int i = 0; i < queue.Count; i++)
        {
            var item = queue[i];
            if (ct.IsCancellationRequested)
            {
                item.State = ItemState.Skipped;
                item.Status = "Durduruldu; çalıştırılmadı";
                continue;
            }

            item.State = ItemState.Running;
            item.Status = "temizleniyor…";
            HeadlineSub.Text = $"{item.Title} ({i + 1}/{queue.Count})";

            // Görev bittikten sonra kuyrukta kalan ilerleme bildirimleri sonucu ezmesin diye.
            bool live = true;
            long diskBefore = disk;
            var progress = new Progress<CleanProgress>(p =>
            {
                if (!live) return;
                if (item.Job.Kind == TaskKind.Disk) HeadlineValue.Text = Format.Bytes(diskBefore + p.Bytes);
                HeadlineSub.Text = p.Files > 0
                    ? $"{item.Title} · {p.Files:N0} dosya · {Format.Bytes(p.Bytes)}"
                    : $"{item.Title} · {Format.Bytes(p.Bytes)}";
            });

            try
            {
                var result = await item.Job.RunAsync(progress, ct);
                item.Status = result.Message;
                item.State = ItemState.Done;
                if (item.Job.Kind == TaskKind.Ram) ram += result.FreedBytes;
                else disk += result.FreedBytes;
            }
            catch (OperationCanceledException)
            {
                item.Status = "Durduruldu";
                item.State = ItemState.Skipped;
            }
            catch (Exception ex)
            {
                item.Status = ex.Message;
                item.State = ItemState.Failed;
            }
            live = false;
            HeadlineValue.Text = Format.Bytes(disk);
            RunProgress.Value = 100.0 * (i + 1) / queue.Count;
        }

        RunProgress.Visibility = Visibility.Collapsed;
        _showingResult = true;
        ShowResult(disk, ram, ct.IsCancellationRequested);
        _cts.Dispose();
        _cts = null;

        UpdateHealth();
        await RefreshProcessesAsync();
        SetBusy(false);
        SweepLabel.Text = "Tekrar temizle";
        await AnalyzeAsync();
    }

    void ShowResult(long disk, long ram, bool cancelled)
    {
        if (cancelled)
        {
            HeadlineCaption.Text = "Durduruldu";
            HeadlineValue.Text = Format.Bytes(disk);
            HeadlineValue.Foreground = Res(disk > 0 ? "Good" : "Text");
            HeadlineSub.Text = "O ana kadar silinenler silindi; kalan görevler çalıştırılmadı.";
            return;
        }
        if (disk == 0 && ram == 0)
        {
            HeadlineCaption.Text = "Sonuç";
            HeadlineValue.Text = "Zaten temiz";
            HeadlineSub.Text = "Silinebilecek bir şey bulunamadı. Ayrıntılar her görevin altında.";
            return;
        }

        HeadlineCaption.Text = "Temizlendi";
        HeadlineValue.Text = Format.Bytes(disk);
        HeadlineValue.Foreground = Res("Good");
        HeadlineSub.Text = ram > 0
            ? $"disk alanı açıldı · ayrıca {Format.Bytes(ram)} RAM boşaldı"
            : "disk alanı açıldı · ayrıntılar her görevin altında";
    }

    void SetBusy(bool busy)
    {
        _busy = busy;
        RefreshButton.IsEnabled = !busy;
        if (busy) SweepLabel.Text = "Durdur";
        UpdateSelection();
    }

    async void SweepButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            _cts?.Cancel();
            SweepLabel.Text = "Durduruluyor…";
            SweepButton.IsEnabled = false;
            return;
        }
        await SweepAsync();
    }

    async void PreviewButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = Selected();
        PreviewButton.IsEnabled = false;
        PreviewLabel.Text = "Hazırlanıyor…";

        const int PerTask = 12;
        var rows = new List<ListRow>();
        long total = 0;
        foreach (var item in selected)
        {
            Preview preview;
            try { preview = await item.Job.PreviewAsync(CancellationToken.None); }
            catch (Exception ex) { preview = new Preview([], ex.Message); }

            long sum = preview.Entries.Sum(x => x.Bytes);
            if (item.Job.Kind == TaskKind.Disk) total += sum;
            rows.Add(new ListRow(item.Title, preview.Note, Format.Bytes(sum), IsHeader: true));

            if (preview.Entries.Count == 0) rows.Add(new ListRow("Silinecek bir şey yok"));
            foreach (var entry in preview.Entries.Take(PerTask))
            {
                rows.Add(entry.IsCommand
                    ? new ListRow("Komut: " + entry.Label, "Aracın kendi temizleme komutu çalıştırılır", Format.Bytes(entry.Bytes))
                    : new ListRow(Format.ShortPath(entry.Label), $"{entry.Files:N0} dosya", Format.Bytes(entry.Bytes)));
            }
            var rest = preview.Entries.Skip(PerTask).ToList();
            if (rest.Count > 0)
                rows.Add(new ListRow($"ve {rest.Count:N0} öğe daha", $"{rest.Sum(x => x.Files):N0} dosya", Format.Bytes(rest.Sum(x => x.Bytes))));
        }

        PreviewLabel.Text = "Neler silinecek?";
        UpdateSelection();

        bool sweep = ListDialog.Show(this, "Neler silinecek?",
            $"{selected.Count} görev · yaklaşık {Format.Bytes(total)} · Bu bir önizleme, henüz hiçbir şey silinmedi.",
            rows,
            "Sadece önbellekler ve geçici dosyalar silinir; kişisel dosyalarınıza dokunulmaz.",
            "Temizle");
        if (sweep) await SweepAsync();
    }

    void StartupButton_Click(object sender, RoutedEventArgs e)
    {
        var items = SystemInfo.GetStartupItems();
        var rows = items.Select(i => new ListRow(i.Name, $"{i.Source} · {i.Command}")).ToList();
        if (rows.Count == 0) rows.Add(new ListRow("Açılışta çalışan program yok"));

        bool open = ListDialog.Show(this, "Başlangıç programları",
            $"Windows açılırken {items.Count} program çalışıyor. Her biri açılışı ve RAM'i biraz daha yavaşlatır.",
            rows,
            "Kapatmak için Görev Yöneticisi → Başlangıç uygulamaları.",
            "Görev Yöneticisi'ni aç");
        if (!open) return;

        try
        {
            Process.Start(new ProcessStartInfo("taskmgr.exe", "/0 /startup") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Dialog.Show(this, "Görev Yöneticisi açılamadı", ex.Message, DialogKind.Error);
        }
    }

    void UptimeButton_Click(object sender, RoutedEventArgs e)
    {
        bool fastStartup = BootHistory.FastStartupEnabled();
        var boots = BootHistory.Recent();
        var rows = boots.Select(b => new ListRow(b.Time.ToString("dd MMMM yyyy, dddd HH:mm"), BootHistory.Describe(b.Kind))).ToList();
        if (rows.Count == 0) rows.Add(new ListRow("Açılış geçmişi okunamadı"));

        var lastFull = boots.FirstOrDefault(b => b.Kind == BootKind.Full);
        string since = lastFull != null ? $"Son tam açılış: {lastFull.Time:dd MMMM HH:mm}. " : "";

        if (!fastStartup)
        {
            ListDialog.Show(this, "Açılış geçmişi", since + "Hızlı Başlangıç kapalı; her \"Kapat\" tam bir kapatmadır.", rows);
            return;
        }

        int fastSinceFull = boots.TakeWhile(b => b.Kind != BootKind.Full).Count(b => b.Kind == BootKind.FastStartup);
        bool open = ListDialog.Show(this, "Hızlı Başlangıç açık",
            since + (fastSinceFull > 0 ? $"O zamandan beri {fastSinceFull} kez kapatıp açtınız ama hiçbiri tam kapatma değildi. " : "") +
            "Hızlı Başlangıç açıkken \"Kapat\" Windows çekirdeğini diske kaydeder; bellek ve sürücüler sıfırlanmaz.",
            rows,
            "Tek seferlik tam kapatma: Shift'e basılıyken Kapat. Kalıcı çözüm: Hızlı başlangıcı aç işaretini kaldırın.",
            "Güç ayarlarını aç");
        if (!open) return;

        try
        {
            // "Güç düğmelerinin yapacaklarını seçin" sayfası; ayarı kullanıcı kendisi değiştirir.
            Process.Start(new ProcessStartInfo("control.exe", "/name Microsoft.PowerOptions /page pageGlobalSettings") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Dialog.Show(this, "Güç ayarları açılamadı", ex.Message, DialogKind.Error);
        }
    }

    void AdminButton_Click(object sender, RoutedEventArgs e)
    {
        if (!Dialog.Confirm(this, "Yönetici olarak yeniden açılsın mı?",
                "Windows Update ve Teslim İyileştirme önbelleklerini temizlemek için yönetici izni gerekir. Windows bir onay penceresi gösterecek.",
                "Yeniden aç"))
            return;

        if (Elevation.RestartAsAdmin()) Application.Current.Shutdown();
    }

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
