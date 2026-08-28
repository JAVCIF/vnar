namespace LocaleGameHub.Models;

public sealed class GameEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Nuevo juego";
    public string ExePath { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string CoverPath { get; set; } = string.Empty;
    public CoverEditState? CoverEdit { get; set; }
    public string VndbId { get; set; } = string.Empty;
    public List<VndbDeveloperRef> Developers { get; set; } = [];
    public bool RunAsAdmin { get; set; }
    public bool Favorite { get; set; }
    public DateTime? LastPlayedUtc { get; set; }

    public string FolderPath => Path.GetDirectoryName(ExePath) ?? string.Empty;
}
