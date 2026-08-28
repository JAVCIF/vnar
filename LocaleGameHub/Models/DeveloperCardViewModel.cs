using LocaleGameHub.Services;

namespace LocaleGameHub.Models;

public sealed class DeveloperCardViewModel
{
    public required DeveloperEntry Developer { get; init; }
    public int GameCount { get; init; }
    public bool HasCover => !string.IsNullOrWhiteSpace(Developer.CoverPath) && File.Exists(Developer.CoverPath);
    public string GameCountLabel => LocalizationService.IsSpanish
        ? $"{GameCount} juego{(GameCount == 1 ? string.Empty : "s")}" 
        : $"{GameCount} game{(GameCount == 1 ? string.Empty : "s")}";
}
