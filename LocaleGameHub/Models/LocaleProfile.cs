namespace LocaleGameHub.Models;

public sealed class LocaleProfile
{
    public string Name { get; init; } = string.Empty;
    public string Guid { get; init; } = string.Empty;
    public string Location { get; init; } = string.Empty;
    public bool RunAsAdmin { get; init; }
}
