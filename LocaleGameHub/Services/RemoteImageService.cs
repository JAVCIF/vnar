using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace LocaleGameHub.Services;

public static class RemoteImageService
{
    private static readonly HttpClient Http = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
        AllowAutoRedirect = true
    })
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    static RemoteImageService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) VNAR/1.0.0-beta.1.1");
        Http.DefaultRequestHeaders.Accept.ParseAdd("image/avif,image/webp,image/apng,image/svg+xml,image/*,*/*;q=0.8");
    }

    public static async Task<string> DownloadCoverAsync(
        string primaryUrl,
        string? fallbackUrl,
        Guid gameId,
        string suffix = "web",
        CancellationToken cancellationToken = default)
    {
        Exception? first = null;
        try
        {
            return await DownloadOneAsync(primaryUrl, gameId, suffix, cancellationToken);
        }
        catch (Exception ex)
        {
            first = ex;
        }

        if (!string.IsNullOrWhiteSpace(fallbackUrl) &&
            !string.Equals(primaryUrl, fallbackUrl, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return await DownloadOneAsync(fallbackUrl, gameId, suffix + "_thumb", cancellationToken);
            }
            catch
            {
                // Throw the first error because it normally contains the more useful remote URL failure.
            }
        }

        throw first ?? new InvalidOperationException(LocalizationService.Bi("No pude descargar la imagen.", "Could not download the image."));
    }

    private static async Task<string> DownloadOneAsync(string url, Guid gameId, string suffix, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException(LocalizationService.Bi("La imagen remota no tiene una URL HTTP/HTTPS válida.", "The remote image does not have a valid HTTP/HTTPS URL."));

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Referrer = TryBuildReferrer(uri);
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var contentType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? string.Empty;
        if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(LocalizationService.IsSpanish ? $"La URL no devolvió una imagen ({contentType})." : $"The URL did not return an image ({contentType}).");

        var extension = ExtensionFromContentType(contentType);
        suffix = Regex.Replace(suffix, "[^a-zA-Z0-9_-]", "_");
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);

        if (extension is ".webp" or ".avif")
        {
            // WPF/WIC support for WebP/AVIF varies by Windows installation. Decode with Skia
            // and normalize to PNG before the file enters VNAR's cover/editor pipeline.
            return ImageCompatibilityService.ConvertStreamToPng(input, gameId, suffix + "_webp");
        }

        var target = CoverService.BuildUniqueCoverPath(gameId, suffix, extension);
        await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await input.CopyToAsync(output, cancellationToken);
        return target;
    }

    public static async Task<string> DownloadUrlFromDropAsync(string url, Guid gameId, CancellationToken cancellationToken = default)
        => await DownloadCoverAsync(url, null, gameId, "drag", cancellationToken);

    private static Uri? TryBuildReferrer(Uri imageUri)
    {
        try { return new Uri(imageUri.GetLeftPart(UriPartial.Authority)); }
        catch { return null; }
    }

    private static string ExtensionFromContentType(string contentType) => contentType switch
    {
        "image/png" => ".png",
        "image/bmp" => ".bmp",
        "image/gif" => ".gif",
        "image/webp" => ".webp",
        "image/avif" => ".avif",
        "image/jpeg" => ".jpg",
        "image/jpg" => ".jpg",
        _ => throw new InvalidOperationException(LocalizationService.IsSpanish ? $"Formato de imagen no compatible: {contentType}" : $"Unsupported image format: {contentType}")
    };
}
