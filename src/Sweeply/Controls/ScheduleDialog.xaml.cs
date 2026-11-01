using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Sweeply.Services;

namespace Sweeply.Controls;

public enum ScheduleChoice { Cancel, Save, Remove }

/// <summary>Haftalık otomatik temizliğin gün ve saatini seçtirir; hangi görevlerin onaysız çalışacağını da gösterir.</summary>
public partial class ScheduleDialog : Window
{
    ScheduleChoice _choice = ScheduleChoice.Cancel;

    public DayOfWeek Day { get; private set; }
    public TimeOnly Time { get; private set; }

    ScheduleDialog(Window owner, bool scheduled, DayOfWeek day, TimeOnly time, IReadOnlyList<string> tasks, IReadOnlyList<string> skipped)
    {
        InitializeComponent();
        Owner = owner;
        Day = day;
        Time = time;

        TitleText.Text = Title = scheduled ? "Otomatik temizlik açık" : "Haftalık otomatik temizlik";
        MessageText.Text = "Sweeply seçilen gün ve saatte açılır ve aşağıdaki görevleri onay sormadan çalıştırır. " +
                           "Bilgisayar o saatte kapalıysa o haftanın temizliği yapılmaz. Sonucu bir sonraki açılışta görürsünüz.";
        ConfirmButton.Content = scheduled ? "Kaydet" : "Zamanla";
        if (scheduled) RemoveButton.Visibility = Visibility.Visible;

        foreach (var d in ScheduleService.Week)
        {
            var chip = Chip("Days", Capitalize(ScheduleService.DayName(d)), d == day);
            chip.Checked += (_, _) => Day = d;
            Days.Children.Add(chip);
        }

        var times = ScheduleService.Times.Select(t => ScheduleService.ParseTime(t)!.Value).ToList();
        if (!times.Contains(time)) times.Add(time);
        foreach (var t in times.OrderBy(t => t))
        {
            var chip = Chip("Times", t.ToString("HH:mm"), t == time);
            chip.Checked += (_, _) => Time = t;
            Times.Children.Add(chip);
        }

        TasksText.Text = tasks.Count > 0 ? string.Join(", ", tasks) : "Seçili görev yok; o gün hiçbir şey silinmez.";
        WarningText.Text = skipped.Count > 0
            ? $"Yönetici izni gerektirdiği için zamanlamada çalışmaz: {string.Join(", ", skipped)}."
            : "";
        if (skipped.Count == 0) WarningText.Visibility = Visibility.Collapsed;

        Loaded += (_, _) => PlayEntrance();
        MouseLeftButtonDown += (_, _) => DragMove();
    }

    RadioButton Chip(string group, string text, bool isChecked) => new()
    {
        Content = text,
        GroupName = group,
        IsChecked = isChecked,
        Style = (Style)FindResource("Chip"),
    };

    static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpper(s[0], new System.Globalization.CultureInfo("tr-TR")) + s[1..];

    void PlayEntrance()
    {
        if (!Motion.Enabled) return;
        var duration = TimeSpan.FromMilliseconds(180);
        Panel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = Motion.EaseOut });
        var grow = new DoubleAnimation(0.96, 1, duration) { EasingFunction = Motion.EaseOut };
        PanelScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        PanelScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
    }

    void Confirm_Click(object sender, RoutedEventArgs e) { _choice = ScheduleChoice.Save; DialogResult = true; }

    void Remove_Click(object sender, RoutedEventArgs e) { _choice = ScheduleChoice.Remove; DialogResult = true; }

    void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    /// <param name="tasks">Zamanlamada çalışacak görevler.</param>
    /// <param name="skipped">Seçili ama yönetici izni gerektiği için zamanlamada çalışmayacak görevler.</param>
    public static (ScheduleChoice Choice, DayOfWeek Day, TimeOnly Time) Show(Window owner, bool scheduled, DayOfWeek day, TimeOnly time,
        IReadOnlyList<string> tasks, IReadOnlyList<string> skipped)
    {
        var dialog = new ScheduleDialog(owner, scheduled, day, time, tasks, skipped);
        dialog.ShowDialog();
        return (dialog._choice, dialog.Day, dialog.Time);
    }
}
