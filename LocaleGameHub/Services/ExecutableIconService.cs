using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LocaleGameHub.Models;

namespace LocaleGameHub.Services;

public static class ExecutableIconService
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(
        string szFileName,
        int nIconIndex,
        IntPtr[]? phiconLarge,
        IntPtr[]? phiconSmall,
        uint nIcons);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static IReadOnlyList<ShortcutIconOption> FindChoices(string gameExePath)
    {
        var result = new List<ShortcutIconOption>();
        var genericPath = ShortcutService.GetGenericIconPath();
        var genericPreview = LoadIconFile(genericPath) ?? CreateFallbackPreview();
        result.Add(new ShortcutIconOption
        {
            DisplayName = LocalizationService.Bi("VNAR · Genérico", "VNAR · Generic"),
            IconPath = genericPath,
            Preview = genericPreview,
            IsGeneric = true
        });

        var folder = Path.GetDirectoryName(gameExePath);
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return result;

        IEnumerable<string> exes;
        try
        {
            exes = Directory.EnumerateFiles(folder, "*.exe", SearchOption.TopDirectoryOnly)
                .OrderByDescending(p => string.Equals(Path.GetFullPath(p), Path.GetFullPath(gameExePath), StringComparison.OrdinalIgnoreCase))
                .ThenBy(p => Path.GetFileName(p), StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch
        {
            return result;
        }

        foreach (var exe in exes)
        {
            try
            {
                if (ExtractIconEx(exe, -1, null, null, 0) == 0) continue;
                var preview = ExtractPreview(exe);
                if (preview is null) continue;

                result.Add(new ShortcutIconOption
                {
                    DisplayName = Path.GetFileName(exe),
                    IconPath = exe,
                    Preview = preview,
                    IsGeneric = false
                });
            }
            catch
            {
                // One broken executable should not block the rest of the choices.
            }
        }

        return result;
    }

    private static ImageSource? ExtractPreview(string exePath)
    {
        var large = new IntPtr[1];
        var count = ExtractIconEx(exePath, 0, large, null, 1);
        if (count == 0 || large[0] == IntPtr.Zero) return null;

        try
        {
            var image = Imaging.CreateBitmapSourceFromHIcon(
                large[0],
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(48, 48));
            image.Freeze();
            return image;
        }
        finally
        {
            DestroyIcon(large[0]);
        }
    }

    private static ImageSource? LoadIconFile(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.OrderByDescending(f => f.PixelWidth).FirstOrDefault();
            frame?.Freeze();
            return frame;
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource CreateFallbackPreview()
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(13, 27, 49)), null, new Rect(0, 0, 64, 64));
            var typeface = new Typeface("Bahnschrift SemiBold");
            var text = new FormattedText(
                "VNAR",
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                14,
                Brushes.White,
                1.0);
            dc.DrawText(text, new Point((64 - text.Width) / 2, (64 - text.Height) / 2));
        }

        var bitmap = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
