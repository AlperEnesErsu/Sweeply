using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Sweeply.Controls;

public enum DialogKind { Info, Question, Warning, Error }

/// <summary>Uygulamanın koyu temasına uyan, MessageBox yerine kullanılan küçük diyalog.</summary>
public partial class Dialog : Window
{
    Dialog(Window owner, DialogKind kind, string title, string message, string confirm, string? cancel)
    {
        InitializeComponent();
        Owner = owner;
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirm;

        if (cancel == null) CancelButton.Visibility = Visibility.Collapsed;
        else CancelButton.Content = cancel;

        (GlyphText.Text, var brushKey) = kind switch
        {
            DialogKind.Question => ("", "Muted"),
            DialogKind.Warning => ("", "Warn"),
            DialogKind.Error => ("", "Danger"),
            _ => ("", "Muted"),
        };
        GlyphText.Foreground = (Brush)FindResource(brushKey);

        Loaded += (_, _) => PlayEntrance();
        MouseLeftButtonDown += (_, _) => DragMove();
    }

    // Modal ortadan belirir: hafif büyüyerek ve saydamlıktan gelerek (0'dan değil, 0.96'dan).
    void PlayEntrance()
    {
        if (!Motion.Enabled) return;
        var duration = TimeSpan.FromMilliseconds(180);
        Panel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = Motion.EaseOut });
        var grow = new DoubleAnimation(0.96, 1, duration) { EasingFunction = Motion.EaseOut };
        PanelScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        PanelScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
    }

    void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    public static bool Confirm(Window owner, string title, string message, string confirm,
        string cancel = "Vazgeç", DialogKind kind = DialogKind.Question) =>
        new Dialog(owner, kind, title, message, confirm, cancel).ShowDialog() == true;

    public static void Show(Window owner, string title, string message, DialogKind kind = DialogKind.Info) =>
        new Dialog(owner, kind, title, message, "Tamam", null).ShowDialog();
}
