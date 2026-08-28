using SkiaSharp;

namespace LocaleGameHub.Services;

/// <summary>
/// Converts image formats that WPF/WIC may not decode reliably (notably WebP)
/// into PNG before they enter the rest of VNAR's cover pipeline.
/// </summary>
public static class ImageCompatibilityService
{
    private static readonly HashSet<string> NeedsNormalization = new(StringComparer.OrdinalIgnoreCase)
    {
        ".webp",
        ".avif"
    };

    public static bool NeedsWpfNormalization(string path)
        => NeedsNormalization.Contains(Path.GetExtension(path));

    public static string NormalizeFileIfNeeded(string sourcePath, Guid ownerId, string suffix)
    {
        if (!NeedsWpfNormalization(sourcePath)) return sourcePath;
        return ConvertFileToPng(sourcePath, ownerId, suffix);
    }

    public static string ConvertFileToPng(string sourcePath, Guid ownerId, string suffix)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException(LocalizationService.Bi("No se encontró la imagen que se iba a convertir.", "The image to convert was not found."), sourcePath);

        using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return ConvertStreamToPng(input, ownerId, suffix);
    }

    public static string ConvertStreamToPng(Stream input, Guid ownerId, string suffix)
    {
        ArgumentNullException.ThrowIfNull(input);

        using var bitmap = SKBitmap.Decode(input);
        if (bitmap is null || bitmap.Width <= 0 || bitmap.Height <= 0)
            throw new InvalidOperationException(LocalizationService.Bi(
                "No pude decodificar esta imagen. El archivo puede estar dañado o usar una variante no compatible.",
                "This image could not be decoded. The file may be damaged or use an unsupported variant."));

        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        if (encoded is null)
            throw new InvalidOperationException(LocalizationService.Bi("No pude convertir la imagen a PNG.", "The image could not be converted to PNG."));

        var target = CoverService.BuildUniqueCoverPath(ownerId, suffix + "_png", ".png");
        using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        encoded.SaveTo(output);
        output.Flush(true);
        return target;
    }
}
