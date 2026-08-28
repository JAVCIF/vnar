namespace LocaleGameHub.Models;

public sealed record CoverSearchResult(
    string Title,
    string ImageUrl,
    string ThumbnailUrl,
    string Source,
    string? VndbId = null,
    string? SourcePageUrl = null);
