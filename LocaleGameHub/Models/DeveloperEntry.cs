namespace LocaleGameHub.Models;

public sealed class DeveloperEntry
{
    public Guid LocalId { get; set; } = Guid.NewGuid();
    public string VndbId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string CoverPath { get; set; } = string.Empty;
    public CoverEditState? CoverEdit { get; set; }
}
