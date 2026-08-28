using System.Net.Http;
using System.Text;
using System.Text.Json;
using LocaleGameHub.Models;

namespace LocaleGameHub.Services;

public sealed class VndbService
{
    private static readonly HttpClient Http = new()
    {
        BaseAddress = new Uri("https://api.vndb.org/kana/"),
        Timeout = TimeSpan.FromSeconds(25)
    };

    static VndbService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("VNAR/0.2.3");
    }

    public async Task<VndbResult?> GetVisualNovelAsync(string rawId, CancellationToken cancellationToken = default)
    {
        var id = NormalizeId(rawId);
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException(LocalizationService.Bi("Escribe un ID de VNDB, por ejemplo v17.", "Enter a VNDB ID, for example v17."));

        var payload = new
        {
            filters = new object[] { "id", "=", id },
            fields = "title,image.url,image.thumbnail,developers{id,name}"
        };

        using var request = BuildPost("vn", payload);
        using var response = await Http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
            return null;

        return ParseVnResult(results[0], id);
    }

    public async Task<Dictionary<string, List<VndbDeveloperRef>>> GetDevelopersForVisualNovelsAsync(
        IEnumerable<string> rawIds,
        CancellationToken cancellationToken = default)
    {
        var ids = rawIds
            .Select(NormalizeId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var map = new Dictionary<string, List<VndbDeveloperRef>>(StringComparer.OrdinalIgnoreCase);
        foreach (var chunk in ids.Chunk(100))
        {
            var batch = chunk.ToList();
            object filters;
            if (batch.Count == 1)
            {
                filters = new object[] { "id", "=", batch[0] };
            }
            else
            {
                var orFilter = new List<object> { "or" };
                orFilter.AddRange(batch.Select(id => (object)new object[] { "id", "=", id }));
                filters = orFilter.ToArray();
            }

            var payload = new
            {
                filters,
                fields = "id,developers{id,name}",
                results = Math.Clamp(batch.Count, 1, 100)
            };

            using var request = BuildPost("vn", payload);
            using var response = await Http.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (doc.RootElement.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in results.EnumerateArray())
                {
                    var id = item.TryGetProperty("id", out var idNode) ? idNode.GetString() ?? string.Empty : string.Empty;
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    map[id] = ParseDevelopers(item);
                }
            }

            foreach (var id in batch)
                map.TryAdd(id, []);
        }

        return map;
    }

    public async Task<IReadOnlyList<CoverSearchResult>> SearchVisualNovelsAsync(
        string query,
        int count = 10,
        CancellationToken cancellationToken = default)
    {
        query = (query ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(query)) return [];
        count = Math.Clamp(count, 1, 20);

        var payload = new
        {
            filters = new object[] { "search", "=", query },
            fields = "id,title,image.url,image.thumbnail",
            sort = "searchrank",
            results = count
        };

        using var request = BuildPost("vn", payload);
        using var response = await Http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            return [];

        var list = new List<CoverSearchResult>();
        foreach (var item in results.EnumerateArray())
        {
            var id = item.TryGetProperty("id", out var idElement) ? idElement.GetString() ?? string.Empty : string.Empty;
            var title = item.TryGetProperty("title", out var titleElement) ? titleElement.GetString() ?? id : id;
            string imageUrl = string.Empty;
            string thumbnailUrl = string.Empty;

            if (item.TryGetProperty("image", out var image) && image.ValueKind == JsonValueKind.Object)
            {
                if (image.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String)
                    imageUrl = url.GetString() ?? string.Empty;
                if (image.TryGetProperty("thumbnail", out var thumbnail) && thumbnail.ValueKind == JsonValueKind.String)
                    thumbnailUrl = thumbnail.GetString() ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(imageUrl) && string.IsNullOrWhiteSpace(thumbnailUrl)) continue;
            if (string.IsNullOrWhiteSpace(imageUrl)) imageUrl = thumbnailUrl;
            if (string.IsNullOrWhiteSpace(thumbnailUrl)) thumbnailUrl = imageUrl;

            list.Add(new CoverSearchResult(
                title,
                imageUrl,
                thumbnailUrl,
                string.IsNullOrWhiteSpace(id) ? "VNDB" : $"VNDB · {id}",
                id,
                string.IsNullOrWhiteSpace(id) ? null : $"https://vndb.org/{id}"));
        }

        return list;
    }

    public async Task<string> DownloadCoverAsync(string imageUrl, Guid gameId, CancellationToken cancellationToken = default)
    {
        AppDataPaths.Ensure();
        var extension = ".jpg";
        try
        {
            var uri = new Uri(imageUrl);
            var ext = Path.GetExtension(uri.AbsolutePath);
            if (ext is ".png" or ".webp" or ".jpg" or ".jpeg") extension = ext;
        }
        catch { }

        var target = CoverService.BuildUniqueCoverPath(gameId, "vndb", extension);
        using var response = await Http.GetAsync(imageUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await input.CopyToAsync(output, cancellationToken);
        return target;
    }

    private static HttpRequestMessage BuildPost(string endpoint, object payload)
        => new(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

    private static VndbResult ParseVnResult(JsonElement item, string fallbackId)
    {
        var id = item.TryGetProperty("id", out var idNode) ? idNode.GetString() ?? fallbackId : fallbackId;
        var title = item.TryGetProperty("title", out var titleElement) ? titleElement.GetString() ?? id : id;
        string? imageUrl = null;

        if (item.TryGetProperty("image", out var image) && image.ValueKind == JsonValueKind.Object)
        {
            if (image.TryGetProperty("url", out var url))
                imageUrl = url.GetString();
            if (string.IsNullOrWhiteSpace(imageUrl) && image.TryGetProperty("thumbnail", out var thumbnail))
                imageUrl = thumbnail.GetString();
        }

        return new VndbResult(id, title, imageUrl, ParseDevelopers(item));
    }

    private static List<VndbDeveloperRef> ParseDevelopers(JsonElement item)
    {
        var list = new List<VndbDeveloperRef>();
        if (!item.TryGetProperty("developers", out var developers) || developers.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var developer in developers.EnumerateArray())
        {
            var id = developer.TryGetProperty("id", out var idNode) ? idNode.GetString() ?? string.Empty : string.Empty;
            var name = developer.TryGetProperty("name", out var nameNode) ? nameNode.GetString() ?? id : id;
            if (string.IsNullOrWhiteSpace(id)) continue;
            list.Add(new VndbDeveloperRef { Id = id, Name = name });
        }

        return list;
    }

    public static string NormalizeId(string value)
    {
        var id = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(id)) return string.Empty;
        if (id.All(char.IsDigit)) id = "v" + id;
        return id;
    }
}

public sealed record VndbResult(string Id, string Title, string? ImageUrl, IReadOnlyList<VndbDeveloperRef> Developers);
