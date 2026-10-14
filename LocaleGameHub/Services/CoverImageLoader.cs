using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LocaleGameHub.Services;

/// <summary>
/// Loads library card covers at the exact size they occupy on screen.
/// Decoding a 1240×1720 cover just to draw it at 230×300 costs ~8 MB of memory
/// per card and leaves the downscale to WPF's default (bilinear) render scaling.
/// Decoding at the card's physical size uses WIC's high-quality scaler instead,
/// so cards are at least as sharp, with a fraction of the memory.
/// Decoding runs on worker threads (a few at a time); results are frozen bitmaps,
/// safe to hand to the UI, and cached. Files are read fully and closed (no lock).
/// </summary>
public static class CoverImageLoader
{
    private const int CacheCapacity = 160;

    private static readonly object Sync = new();
    private static readonly Dictionary<string, LinkedListNode<(string Key, ImageSource Image)>> CacheIndex = new(StringComparer.OrdinalIgnoreCase);
    private static readonly LinkedList<(string Key, ImageSource Image)> CacheOrder = new();

    // Enough to keep a page filling quickly without starving the UI of CPU.
    private static readonly SemaphoreSlim DecodeSlots = new(Math.Clamp(Environment.ProcessorCount / 2, 2, 4));

    private static double _dpiScale = 1.0;

    /// <summary>Monitor scale of the library window (1.0 = 96 DPI, 1.5 = 150 %).</summary>
    public static double DpiScale
    {
        get => _dpiScale;
        set
        {
            if (value <= 0 || Math.Abs(value - _dpiScale) < 0.001) return;
            _dpiScale = value;
            ClearCache(); // cached bitmaps were sized for the old scale
        }
    }

    public static void ClearCache()
    {
        lock (Sync)
        {
            CacheIndex.Clear();
            CacheOrder.Clear();
        }
    }

    /// <summary>
    /// Returns the cover immediately if it is already cached (no disk access beyond
    /// a file stat). Used when building cards so revisited pages appear instantly.
    /// </summary>
    public static ImageSource? TryGetCached(string? path, double boxWidthDip, double boxHeightDip)
    {
        var key = BuildKey(path, boxWidthDip, boxHeightDip, out _);
        if (key is null) return null;
        lock (Sync)
        {
            if (!CacheIndex.TryGetValue(key, out var hit)) return null;
            CacheOrder.Remove(hit);
            CacheOrder.AddFirst(hit);
            return hit.Value.Image;
        }
    }

    /// <summary>Decodes on a worker thread. Returns null if missing, unreadable or cancelled.</summary>
    public static async Task<ImageSource?> LoadAsync(string? path, double boxWidthDip, double boxHeightDip, CancellationToken cancellationToken)
    {
        try
        {
            await DecodeSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }

        try
        {
            if (cancellationToken.IsCancellationRequested) return null;
            return await Task.Run(() => Load(path, boxWidthDip, boxHeightDip), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            DecodeSlots.Release();
        }
    }

    /// <summary>Synchronous load with caching. Callers on the UI thread should prefer LoadAsync.</summary>
    public static ImageSource? Load(string? path, double boxWidthDip, double boxHeightDip)
    {
        var key = BuildKey(path, boxWidthDip, boxHeightDip, out var fullPath);
        if (key is null) return null;

        lock (Sync)
        {
            if (CacheIndex.TryGetValue(key, out var hit))
            {
                CacheOrder.Remove(hit);
                CacheOrder.AddFirst(hit);
                return hit.Value.Image;
            }
        }

        var image = Decode(fullPath, boxWidthDip, boxHeightDip);
        if (image is null) return null;

        lock (Sync)
        {
            if (!CacheIndex.ContainsKey(key))
            {
                CacheIndex[key] = CacheOrder.AddFirst((key, image));
                while (CacheOrder.Count > CacheCapacity)
                {
                    var last = CacheOrder.Last!;
                    CacheOrder.RemoveLast();
                    CacheIndex.Remove(last.Value.Key);
                }
            }
        }

        return image;
    }

    private static string? BuildKey(string? path, double boxWidthDip, double boxHeightDip, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path)) return null;

        FileInfo info;
        try
        {
            info = new FileInfo(path);
            if (!info.Exists) return null;
        }
        catch
        {
            return null;
        }

        fullPath = info.FullName;
        // Size and timestamp in the key: a file replaced in place is reloaded.
        return string.Create(CultureInfo.InvariantCulture,
            $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{boxWidthDip}x{boxHeightDip}@{_dpiScale}");
    }

    private static ImageSource? Decode(string path, double boxWidthDip, double boxHeightDip)
    {
        try
        {
            // Read once into memory: the file is closed immediately and can be
            // replaced or deleted by the cover editor while the card is visible.
            var bytes = File.ReadAllBytes(path);

            int pixelWidth, pixelHeight;
            using (var header = new MemoryStream(bytes, writable: false))
            {
                // DelayCreation reads only the header to get the pixel size.
                var frame = BitmapFrame.Create(header, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                pixelWidth = frame.PixelWidth;
                pixelHeight = frame.PixelHeight;
            }

            var (decodeWidth, decodeHeight) = CoverDecodeMath.ForFill(
                pixelWidth, pixelHeight, boxWidthDip, boxHeightDip, _dpiScale);

            // With CacheOption.OnLoad the pixels are fully decoded by EndInit, so the
            // stream can (and should) be disposed right after.
            using var stream = new MemoryStream(bytes, writable: false);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            if (decodeWidth > 0) bitmap.DecodePixelWidth = decodeWidth;
            else if (decodeHeight > 0) bitmap.DecodePixelHeight = decodeHeight;
            bitmap.EndInit();
            bitmap.Freeze(); // frozen: usable from the UI thread although decoded elsewhere
            return bitmap;
        }
        catch
        {
            // Unreadable or unsupported image: the card shows its title placeholder.
            return null;
        }
    }
}
