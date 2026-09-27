using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Sweeply.Controls;

/// <summary>Uygulama genelinde ortak hareket ayarları. Windows'ta "Animasyon efektleri" kapalıysa hareket devre dışı kalır.</summary>
public static class Motion
{
    public static bool Enabled => SystemParameters.ClientAreaAnimation;

    public static readonly IEasingFunction EaseOut = Freeze(new CubicEase { EasingMode = EasingMode.EaseOut });

    static IEasingFunction Freeze(CubicEase ease)
    {
        ease.Freeze();
        return ease;
    }

    /// <summary>Öğeyi 8px aşağıdan, saydamlıktan gelerek gösterir.</summary>
    public static void FadeUp(UIElement element, int delayMs = 0, int durationMs = 260)
    {
        if (!Enabled) return;

        var move = new TranslateTransform(0, 8);
        element.RenderTransform = move;
        element.Opacity = 0;

        var begin = TimeSpan.FromMilliseconds(delayMs);
        var duration = TimeSpan.FromMilliseconds(durationMs);
        var fade = new DoubleAnimation(0, 1, duration) { BeginTime = begin, EasingFunction = EaseOut };
        var slide = new DoubleAnimation(8, 0, duration) { BeginTime = begin, EasingFunction = EaseOut };
        element.BeginAnimation(UIElement.OpacityProperty, fade);
        move.BeginAnimation(TranslateTransform.YProperty, slide);
    }

    /// <summary>Bir sayıyı sıfırdan hedefe doğru sayarak metne yazar (ör. temizlenen alan).</summary>
    public static async Task CountUpAsync(TextBlock target, long value, Func<long, string> format, int durationMs = 700)
    {
        if (!Enabled || value <= 0)
        {
            target.Text = format(value);
            return;
        }

        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < durationMs)
        {
            double t = clock.ElapsedMilliseconds / (double)durationMs;
            double eased = 1 - Math.Pow(1 - t, 3);
            target.Text = format((long)(value * eased));
            await Task.Delay(16);
        }
        target.Text = format(value);
    }
}
