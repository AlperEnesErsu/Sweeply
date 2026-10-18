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
    readonly HealthHistory _history = new();
    readonly AppSettings _settings = AppSettings.Load();
    CancellationTokenSource? _cts;
    bool _busy;
    bool _showingResult;
    int _tick;

    public MainWindow(bool autoRun)
    {
        InitializeComponent();
        _autoRun = autoRun;

        BuildTasks();

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

    /// <summary>Kapanırken geçmişi diske yazar; en fazla 10 dakikalık aralık bundan kaybı sınırlar.</summary>
    protected override void OnClosed(EventArgs e)
    {
        _history.Save(DateTime.Now);
        base.OnClosed(e);
    }

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

    /// <summary>Katalogdan görevleri kurar. Son seçim ayarlardan okunur; yoksa her görevin varsayılanı kullanılır.</summary>
    void BuildTasks()
    {
        foreach (var item in _items) item.PropertyChanged -= OnItemChanged;
        _items.Clear();

        foreach (var job in Catalog.All(_settings))
        {
            var item = new CleanupItem(job)
            {
                IsEnabled = _settings.SelectedTasks?.Contains(job.Title) ?? job.EnabledByDefault,
            };
            item.PropertyChanged += OnItemChanged;
            _items.Add(item);
        }
    }

    void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CleanupItem.IsEnabled))
        {
            // Seçim her değiştiğinde kaydedilir; bir sonraki açılışta aynı görevler işaretli gelir.
            _settings.SelectedTasks = _items.Where(i => i.IsEnabled).Select(i => i.Job.Title).ToList();
            _settings.Save();
        }

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
        HeadlineSub.Text = Summaries.SelectionSub(selected.Count);
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
        var now = DateTime.Now;
        _history.Record(s.DiskUsedPercent, s.RamLoad, now);
        // Anlık "dolu" uyarısı, eğilim uyarısından önce gelir: ikisi de varsa dolu olan öne çıkar.
        ShowNote(DiskBar, DiskSub,
            HealthTips.Disk(s) ?? HealthTips.DiskTrend(_history.Samples, DateOnly.FromDateTime(now)),
            $"{Format.Bytes(s.DiskTotal)} kapasite");

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
        var extra = _settings.ProtectedProcesses.ToList();
        var top = await Task.Run(() => ProcessService.GetTop(12, extra));
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
        item.Estimate = Summaries.Estimate(analysis);
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
            HeadlineSub.Text = Summaries.Running(item.Title, i, queue.Count);

            // Görev bittikten sonra kuyrukta kalan ilerleme bildirimleri sonucu ezmesin diye.
            bool live = true;
            long diskBefore = disk;
            var progress = new Progress<CleanProgress>(p =>
            {
                if (!live) return;
                if (item.Job.Kind == TaskKind.Disk) HeadlineValue.Text = Format.Bytes(diskBefore + p.Bytes);
                HeadlineSub.Text = Summaries.Progress(item.Title, p);
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
        ShowHeadline(Summaries.SweepResult(disk, ram, ct.IsCancellationRequested));
        _cts.Dispose();
        _cts = null;

        UpdateHealth();
        await RefreshProcessesAsync();
        SetBusy(false);
        SweepLabel.Text = "Tekrar temizle";
        await AnalyzeAsync();
    }

    void ShowHeadline(Headline h)
    {
        HeadlineCaption.Text = h.Caption;
        HeadlineValue.Text = h.Value;
        HeadlineValue.Foreground = Res(h.Tone == Tone.Good ? "Good" : "Text");
        HeadlineSub.Text = h.Sub;
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

        var previews = new List<(string, TaskKind, Preview)>();
        foreach (var item in selected)
        {
            Preview preview;
            try { preview = await item.Job.PreviewAsync(CancellationToken.None); }
            catch (Exception ex) { preview = new Preview([], ex.Message); }
            previews.Add((item.Title, item.Job.Kind, preview));
        }
        var (rows, total) = Summaries.PreviewRows(previews);

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
        // Devre dışı öğeler de listelenir ki yeniden etkinleştirilebilsin; sayaç yalnızca etkin olanları sayar.
        var items = StartupService.GetItems(includeDisabled: true);
        var rows = items.Select(i => new ListRow(i.Name, $"{i.Source} · {i.Command}") { IsChecked = i.Enabled }).ToList();
        if (rows.Count == 0) rows.Add(new ListRow("Açılışta çalışan program yok"));

        bool apply = ListDialog.Show(this, "Başlangıç programları",
            $"Windows açılırken {items.Count(i => i.Enabled)} program çalışıyor. İşaretini kaldırdığınız program açılışta çalışmaz; silinmez.",
            rows,
            "Değişiklikler hemen uygulanır ve Görev Yöneticisi'nde de aynı görünür.",
            "Uygula");
        if (!apply) return;

        var failed = new List<string>();
        for (int i = 0; i < items.Count; i++)
        {
            bool wanted = rows[i].IsChecked == true;
            if (wanted == items[i].Enabled) continue;
            if (!StartupService.SetEnabled(items[i], wanted)) failed.Add(items[i].Name);
        }

        UpdateHealth();
        if (failed.Count > 0)
            Dialog.Show(this, "Bazı değişiklikler uygulanamadı",
                $"{string.Join(", ", failed)} için yazma izni yok. Tüm bilgisayarı etkileyen öğeler için Sweeply'i yönetici olarak açın.",
                DialogKind.Warning);
    }

    void UptimeButton_Click(object sender, RoutedEventArgs e)
    {
        bool fastStartup = BootHistory.FastStartupEnabled();
        var boots = BootHistory.Recent();
        var rows = boots.Select(b => new ListRow(b.Time.ToString("dd MMMM yyyy, dddd HH:mm"), BootHistory.Describe(b.Kind))).ToList();
        if (rows.Count == 0) rows.Add(new ListRow("Açılış geçmişi okunamadı"));

        var (title, message) = Summaries.BootSummary(boots, fastStartup);
        if (!fastStartup)
        {
            ListDialog.Show(this, title, message, rows);
            return;
        }

        bool open = ListDialog.Show(this, title, message,
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

    /// <summary>
    /// Kendi temizlik klasörleri ve hariç tutulanlar. Bir satırın işaretini kaldırıp kapatmak o klasörü listeden çıkarır.
    /// "Klasör ekle" klasörü seçtirir ve temizlenip temizlenmeyeceğini sorar; vazgeçilirse klasör hariç tutulur.
    /// </summary>
    async void FoldersButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;

        var custom = _settings.CustomFolders.ToList();
        var excluded = _settings.ExcludedFolders.ToList();
        var customRows = custom.Select(f => new ListRow(f, "Temizlik klasörü · 24 saatten eski dosyalar") { IsChecked = true }).ToList();
        var excludedRows = excluded.Select(f => new ListRow(f, "Hariç tutulan · hiçbir görev dokunmaz") { IsChecked = true }).ToList();
        var rows = customRows.Concat(excludedRows).ToList();
        if (rows.Count == 0) rows.Add(new ListRow("Henüz klasör eklenmedi"));

        bool add = ListDialog.Show(this, "Temizlik klasörleri",
            "Bir klasörün işaretini kaldırıp kapatırsanız listeden çıkar.",
            rows,
            "Klasör ekleyip temizlenmesini ya da hariç tutulmasını seçersiniz.",
            "Klasör ekle");

        // Kapatma düğmesiyle de olsa, işareti kaldırılanlar listeden çıkarılır.
        _settings.CustomFolders = custom.Where((_, i) => customRows[i].IsChecked == true).ToList();
        _settings.ExcludedFolders = excluded.Where((_, i) => excludedRows[i].IsChecked == true).ToList();

        if (add)
        {
            var picker = new Microsoft.Win32.OpenFolderDialog { Title = "Temizlenecek ya da hariç tutulacak klasörü seçin" };
            if (picker.ShowDialog(this) == true)
            {
                var path = Path.GetFullPath(picker.FolderName);
                var error = FolderRules.Validate(path);
                bool known = _settings.CustomFolders.Contains(path, StringComparer.OrdinalIgnoreCase)
                             || _settings.ExcludedFolders.Contains(path, StringComparer.OrdinalIgnoreCase);

                if (error != null)
                    Dialog.Show(this, "Klasör eklenemedi", error, DialogKind.Warning);
                else if (known)
                    Dialog.Show(this, "Zaten ekli", "Bu klasör zaten listede.");
                else if (Dialog.Confirm(this, "Bu klasör nasıl kullanılsın?",
                             $"{path}\n\nTemizlensin: içindeki 24 saatten eski dosyalar silinir.\nHariç tut: hiçbir görev bu klasöre dokunmaz.",
                             "Temizlensin", "Hariç tut"))
                {
                    _settings.CustomFolders.Add(path);
                    // Yeni eklenen klasör bu oturumda hemen işaretli gelsin diye seçime de eklenir.
                    // Seçim henüz kaydedilmediyse (varsayılanlar) önce mevcut durumdan başlanır; yoksa diğerleri sıfırlanırdı.
                    _settings.SelectedTasks ??= _items.Where(i => i.IsEnabled).Select(i => i.Job.Title).ToList();
                    if (!_settings.SelectedTasks.Contains("Kendi klasörlerim")) _settings.SelectedTasks.Add("Kendi klasörlerim");
                }
                else
                {
                    _settings.ExcludedFolders.Add(path);
                }
            }
        }

        _settings.Save();
        BuildTasks();
        UpdateSelection();
        await AnalyzeAsync();
    }

    /// <summary>Haftalık otomatik temizliği açar veya kapatır (Görev Zamanlayıcı'daki görev).</summary>
    async void ScheduleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        ScheduleButton.IsEnabled = false;
        try
        {
            if (await ScheduleService.IsScheduledAsync())
            {
                if (!Dialog.Confirm(this, "Otomatik temizlik kapatılsın mı?",
                        "Zamanlanmış görev silinir. Sweeply'i elle açmaya devam edebilirsiniz.",
                        "Kapat", kind: DialogKind.Warning))
                    return;

                var error = await ScheduleService.RemoveAsync();
                if (error != null) Dialog.Show(this, "Kapatılamadı", error, DialogKind.Error);
                return;
            }

            if (!Dialog.Confirm(this, "Haftalık otomatik temizlik açılsın mı?",
                    $"Her pazar {ScheduleService.Time}'da seçili görevler onay sormadan çalışır. Neyin silineceğini önce \"Neler silinecek?\" ile görebilirsiniz. Yönetici izni gerektiren görevler bu zamanlamada çalışmaz.",
                    "Zamanla"))
                return;

            var createError = await ScheduleService.CreateAsync(Environment.ProcessPath);
            if (createError != null) Dialog.Show(this, "Zamanlanamadı", createError, DialogKind.Error);
            else Dialog.Show(this, "Zamanlandı", $"Her pazar {ScheduleService.Time}'da Sweeply otomatik olarak açılır ve seçili görevleri çalıştırır.");
        }
        finally
        {
            ScheduleButton.IsEnabled = true;
        }
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

        var outcome = await ProcessService.CloseGracefullyAsync(group.Name);
        if (outcome.Remaining > 0)
        {
            // Pencere olmayan bir işleme "kapan" mesajı ulaşmaz; bunu ayrıca söylemek kullanıcıyı yanıltmaz.
            string message = outcome.HadWindow
                ? $"{outcome.Remaining} işlem kapanmadı. Zorla kapatırsanız kaydedilmemiş veriler kaybolabilir."
                : "Bu program pencere açmıyor, arka planda çalışıyor. Zorla kapatmak gerekebilir.";
            if (Dialog.Confirm(this, $"{group.Name} hâlâ açık", message, "Zorla kapat", kind: DialogKind.Warning))
                ProcessService.Kill(group.Name);
        }

        await Task.Delay(500);
        UpdateHealth();
        await RefreshProcessesAsync();
    }
}
