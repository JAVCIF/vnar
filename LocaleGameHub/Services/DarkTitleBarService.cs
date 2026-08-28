using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LocaleGameHub.Services;

public static class DarkTitleBarService
{
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;
    private const int DwmwaUseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    public static void Apply(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                var enabled = 1;
                var result = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int));
                if (result != 0)
                    DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeBefore20H1, ref enabled, sizeof(int));
            }
            catch
            {
                // Unsupported Windows versions simply keep their system title bar.
            }
        };
    }
}
