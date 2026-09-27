using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Sweeply.Services;

/// <summary>Windows 10/11 başlık çubuğunu uygulamanın koyu temasına uydurur. Desteklenmeyen sürümlerde sessizce hiçbir şey yapmaz.</summary>
public static class WindowTheme
{
    const int DwmwaUseImmersiveDarkMode = 20;
    const int DwmwaBorderColor = 34;
    const int DwmwaCaptionColor = 35;

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void ApplyDark(Window window, Color caption, Color border)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        int on = 1;
        DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref on, sizeof(int));

        int captionRef = ToColorRef(caption);
        DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref captionRef, sizeof(int));

        int borderRef = ToColorRef(border);
        DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref borderRef, sizeof(int));
    }

    static int ToColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);
}
