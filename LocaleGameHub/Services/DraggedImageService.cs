using System.Net;
using System.Text.RegularExpressions;
using System.Windows;

namespace LocaleGameHub.Services;

/// <summary>
/// Normalizes image drag-and-drop data coming from Windows Explorer and browsers
/// such as Chrome/Edge/Google Images.
/// </summary>
public static class DraggedImageService
{
    public static bool TryGetImage(IDataObject data, out string? localFile, out string? remoteUrl)
    {
        localFile = null;
        remoteUrl = null;

        if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files)
        {
            var file = files.FirstOrDefault(f => File.Exists(f) && CoverService.IsImage(f));
            if (!string.IsNullOrWhiteSpace(file))
            {
                localFile = file;
                return true;
            }
        }

        // Chrome/Edge generally expose an HTML fragment for dragged images.
        try
        {
            if (data.GetDataPresent(DataFormats.Html) && data.GetData(DataFormats.Html) is string html)
            {
                var match = Regex.Match(
                    html,
                    "<img[^>]+(?:src|data-src|data-iurl|data-original)\\s*=\\s*[\\\"'](?<url>https?://[^\\\"']+)",
                    RegexOptions.IgnoreCase);

                if (match.Success && TryNormalizeHttpUrl(WebUtility.HtmlDecode(match.Groups["url"].Value), out var url))
                {
                    remoteUrl = url;
                    return true;
                }

                // Google Images can wrap the original image URL inside imgurl/mediaurl.
                match = Regex.Match(
                    html,
                    "(?:imgurl|mediaurl)=(?<url>https?%3A%2F%2F[^&\"'<>]+|https?://[^&\"'<>]+)",
                    RegexOptions.IgnoreCase);

                if (match.Success)
                {
                    var encoded = WebUtility.HtmlDecode(match.Groups["url"].Value);
                    var decoded = encoded.Contains('%') ? Uri.UnescapeDataString(encoded) : encoded;
                    if (TryNormalizeHttpUrl(decoded, out url))
                    {
                        remoteUrl = url;
                        return true;
                    }
                }
            }
        }
        catch
        {
            // Browser drag formats vary. Continue trying the remaining formats.
        }

        foreach (var format in new[]
                 {
                     "UniformResourceLocatorW",
                     "UniformResourceLocator",
                     DataFormats.UnicodeText,
                     DataFormats.Text
                 })
        {
            try
            {
                if (!data.GetDataPresent(format)) continue;
                var value = data.GetData(format)?.ToString()?.Trim().Trim('\0');
                if (TryNormalizeHttpUrl(value, out var url))
                {
                    remoteUrl = url;
                    return true;
                }
            }
            catch
            {
                // Keep trying other formats.
            }
        }

        return false;
    }

    private static bool TryNormalizeHttpUrl(string? value, out string url)
    {
        url = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;

        value = WebUtility.HtmlDecode(value.Trim());
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;

        // A Google Images /imgres URL usually contains the actual image in imgurl/mediaurl.
        if (uri.Host.Contains("google.", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var pair = part.Split('=', 2);
                if (pair.Length != 2) continue;

                var key = Uri.UnescapeDataString(pair[0]);
                if (!key.Equals("imgurl", StringComparison.OrdinalIgnoreCase) &&
                    !key.Equals("mediaurl", StringComparison.OrdinalIgnoreCase))
                    continue;

                var candidate = Uri.UnescapeDataString(pair[1]);
                if (Uri.TryCreate(candidate, UriKind.Absolute, out var imageUri) &&
                    imageUri.Scheme is "http" or "https")
                {
                    url = imageUri.AbsoluteUri;
                    return true;
                }
            }
        }

        url = uri.AbsoluteUri;
        return true;
    }
}
