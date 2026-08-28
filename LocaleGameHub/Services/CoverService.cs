using System.Diagnostics;
using System.Windows.Media.Imaging;

namespace LocaleGameHub.Services;

public static class CoverService
{
    private static readonly string[] Supported = [".jpg", ".jpeg", ".png", ".bmp", ".webp", ".gif"];

    public static bool IsImage(string path) => Supported.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static string BuildUniqueCoverPath(Guid gameId, string suffix, string extension)
    {
        AppDataPaths.Ensure();
        suffix = SanitizeSuffix(suffix);
        extension = extension.StartsWith('.') ? extension : "." + extension;
        return Path.Combine(AppDataPaths.CoversDir, $"{gameId:N}_{suffix}_{Guid.NewGuid():N}{extension.ToLowerInvariant()}");
    }

    public static string ResolveEditableSource(Guid gameId, string? currentCoverPath)
    {
        if (!string.IsNullOrWhiteSpace(currentCoverPath) && File.Exists(currentCoverPath) && !LooksLikeRenderedCover(currentCoverPath))
            return ImageCompatibilityService.NormalizeFileIfNeeded(currentCoverPath, gameId, "compat");

        AppDataPaths.Ensure();
        var prefix = gameId.ToString("N") + "_";
        var renderedTime = DateTime.MaxValue;
        try
        {
            if (!string.IsNullOrWhiteSpace(currentCoverPath) && File.Exists(currentCoverPath))
                renderedTime = File.GetLastWriteTimeUtc(currentCoverPath);
        }
        catch { }

        try
        {
            var candidates = Directory.EnumerateFiles(AppDataPaths.CoversDir)
                .Where(File.Exists)
                .Where(path => Path.GetFileName(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Where(path => IsImage(path))
                .Where(path => !LooksLikeRenderedCover(path))
                .Select(path => new
                {
                    Path = path,
                    Time = SafeLastWriteTimeUtc(path)
                })
                .Where(x => x.Time <= renderedTime.AddSeconds(2))
                .OrderByDescending(x => x.Time)
                .ToList();

            if (candidates.Count > 0)
                return ImageCompatibilityService.NormalizeFileIfNeeded(candidates[0].Path, gameId, "compat");
        }
        catch { }

        return !string.IsNullOrWhiteSpace(currentCoverPath) && File.Exists(currentCoverPath)
            ? ImageCompatibilityService.NormalizeFileIfNeeded(currentCoverPath, gameId, "compat")
            : string.Empty;
    }

    private static bool LooksLikeRenderedCover(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return name.Contains("_edited", StringComparison.OrdinalIgnoreCase)
            || name.Contains("_rendered", StringComparison.OrdinalIgnoreCase);
    }

    private static DateTime SafeLastWriteTimeUtc(string path)
    {
        try { return File.GetLastWriteTimeUtc(path); }
        catch { return DateTime.MinValue; }
    }

    public static string ImportCover(string source, Guid gameId)
    {
        if (!File.Exists(source) || !IsImage(source))
            throw new InvalidOperationException(LocalizationService.Bi("Selecciona una imagen JPG, PNG, BMP, GIF o WEBP válida.", "Select a valid JPG, PNG, BMP, GIF, or WEBP image."));

        var ext = Path.GetExtension(source).ToLowerInvariant();
        if (ImageCompatibilityService.NeedsWpfNormalization(source))
            return ImageCompatibilityService.ConvertFileToPng(source, gameId, "local_webp");

        var target = BuildUniqueCoverPath(gameId, "local", ext);
        File.Copy(source, target, false);
        return target;
    }

    public static string SaveBitmapCover(BitmapSource bitmap, Guid gameId, string suffix = "edited")
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        var target = BuildUniqueCoverPath(gameId, suffix, ".png");
        var temp = target + ".tmp";

        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                encoder.Save(stream);
                stream.Flush(true);
            }

            File.Move(temp, target);
            return target;
        }
        catch
        {
            try
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
            catch { }
            throw;
        }
    }

    public static void OpenGoogleImages(string query)
    {
        var url = "https://www.google.com/search?tbm=isch&q=" + Uri.EscapeDataString(query);
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private static string SanitizeSuffix(string suffix)
    {
        if (string.IsNullOrWhiteSpace(suffix)) return "cover";
        var chars = suffix.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray();
        return new string(chars);
    }
}
