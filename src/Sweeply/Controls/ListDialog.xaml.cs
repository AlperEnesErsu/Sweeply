using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Sweeply.Controls;

/// <param name="IsHeader">Bölüm başlığı satırı (kalın, üstünde boşluk).</param>
public sealed record ListRow(string Primary, string? Secondary = null, string? Right = null, bool IsHeader = false);

/// <summary>Önizleme ve başlangıç programları gibi uzun listeleri gösteren, temaya uygun pencere.</summary>
public partial class ListDialog : Window
{
    ListDialog(Window owner, string title, string subtitle, IEnumerable<ListRow> rows, string footer, string? actionText)
    {
        InitializeComponent();
        Owner = owner;
        Title = title;  // görünmez ama ekran okuyucular ve UI Automation pencereyi bu adla tanır
        TitleText.Text = title;
        SubtitleText.Text = subtitle;
        FooterText.Text = footer;
        Rows.ItemsSource = rows.ToList();

        if (actionText != null)
        {
            ActionButton.Content = actionText;
            ActionButton.Visibility = Visibility.Visible;
        }

        Header.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        Loaded += (_, _) => PlayEntrance();
    }

    void PlayEntrance()
    {
        if (!Motion.Enabled) return;
        var duration = TimeSpan.FromMilliseconds(180);
        Panel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = Motion.EaseOut });
        var grow = new DoubleAnimation(0.97, 1, duration) { EasingFunction = Motion.EaseOut };
        PanelScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        PanelScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
    }

    void Close_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    void Action_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    /// <returns>Kullanıcı eylem butonuna bastıysa true.</returns>
    public static bool Show(Window owner, string title, string subtitle, IEnumerable<ListRow> rows,
        string footer = "", string? actionText = null) =>
        new ListDialog(owner, title, subtitle, rows, footer, actionText).ShowDialog() == true;
}
