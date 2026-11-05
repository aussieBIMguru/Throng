using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Throng.App.Services;

/// <summary>
/// Asks Windows 10 (20H1+) / 11 to draw a dark title bar so the chrome matches the theme. Silently does nothing
/// on systems that don't support it.
/// </summary>
public static class DarkTitleBar
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmUseImmersiveDarkModeLegacy = 19;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Applies the dark title bar. Call from <see cref="Window.SourceInitialized"/>.</summary>
    public static void Apply(Window window)
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            int enabled = 1;
            if (DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkModeLegacy, ref enabled, sizeof(int));
            }
        }
        catch (DllNotFoundException)
        {
            // Not available on this OS; cosmetic only.
        }
        catch (EntryPointNotFoundException)
        {
            // Not available on this OS; cosmetic only.
        }
    }
}
