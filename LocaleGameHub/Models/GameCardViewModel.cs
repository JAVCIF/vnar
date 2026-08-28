using LocaleGameHub.Services;

namespace LocaleGameHub.Models;

public sealed class GameCardViewModel
{
    public required GameEntry Game { get; init; }
    public string AdminLabel => LocalizationService.Bi("Ejecutar como administrador", "Run as administrator");
    public string PlayLabel => LocalizationService.Bi("▶ Jugar", "▶ Play");
    public string EditTooltip => LocalizationService.Bi("Editar juego", "Edit game");
    public bool HasCover => !string.IsNullOrWhiteSpace(Game.CoverPath) && File.Exists(Game.CoverPath);
    public string FavoriteTooltip => Game.Favorite
        ? LocalizationService.Bi("Quitar de favoritos", "Remove from favorites")
        : LocalizationService.Bi("Añadir a favoritos", "Add to favorites");
}
