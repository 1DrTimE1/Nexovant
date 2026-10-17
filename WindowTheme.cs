using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Nexovant;

/// <summary>Gives a window a dark title bar that matches the app background (Windows 10 20H1+ / Windows 11).</summary>
public static class WindowTheme
{
    const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    const int DWMWA_CAPTION_COLOR = 35;

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void Apply(Window window)
    {
        window.Background = (Brush)Application.Current.Resources["BgBrush"];
        window.FontFamily = new FontFamily("Segoe UI");
        window.FontSize = 13;
        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            int on = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));

            // COLORREF is 0x00BBGGRR. Ignored on Windows 10, where the dark mode above still applies.
            var bg = (Color)Application.Current.Resources["BgColor"];
            int caption = bg.R | (bg.G << 8) | (bg.B << 16);
            DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
        };
    }
}
