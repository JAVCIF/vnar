using System.Net.Http;
using System.Text.Json;
using LocaleGameHub.Models;

namespace LocaleGameHub.Services;

public sealed class ImageSearchService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(25)
    };

    static ImageSearchService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("VNAR/0.2.0");
    }

    public async Task<IReadOnlyList<CoverSearchResult>> SearchGoogleImagesAsync(
        string query,
        string apiKey,
        int count = 10,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException(LocalizationService.Bi("Configura una API key de SerpApi en Ajustes para usar Google Images dentro del Hub.", "Configure a SerpApi API key in Settings to use Google Images inside VNAR."));
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var url = "https://serpapi.com/search.json?engine=google_images&q="
                  + Uri.EscapeDataString(query)
                  + "&ijn=0&safe=active&api_key="
                  + Uri.EscapeDataString(apiKey.Trim());

        using var response = await Http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (doc.RootElement.TryGetProperty("error", out var errorElement))
            throw new InvalidOperationException(errorElement.GetString() ?? LocalizationService.Bi("SerpApi devolvió un error.", "SerpApi returned an error."));

        if (!doc.RootElement.TryGetProperty("images_results", out var results) || results.ValueKind != JsonValueKind.Array)
            return [];

        var list = new List<CoverSearchResult>();
        foreach (var item in results.EnumerateArray())
        {
            if (list.Count >= count) break;

            var original = ReadString(item, "original");
            var thumb = ReadString(item, "thumbnail");
            var title = ReadString(item, "title");
            var source = ReadString(item, "source");
            var link = ReadString(item, "link");

            if (string.IsNullOrWhiteSpace(thumb) && string.IsNullOrWhiteSpace(original))
                continue;

            var imageUrl = string.IsNullOrWhiteSpace(original) ? thumb : original;
            var thumbnailUrl = string.IsNullOrWhiteSpace(thumb) ? imageUrl : thumb;
            list.Add(new CoverSearchResult(
                string.IsNullOrWhiteSpace(title) ? query : title,
                imageUrl,
                thumbnailUrl,
                string.IsNullOrWhiteSpace(source) ? "Google Images" : source,
                null,
                link));
        }

        return list;
    }

    private static string ReadString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
