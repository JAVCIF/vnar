using System.Windows.Media;

namespace LocaleGameHub.Models;

public sealed class ShortcutIconOption
{
    public required string DisplayName { get; init; }
    public required string IconPath { get; init; }
    public required ImageSource Preview { get; init; }
    public bool IsGeneric { get; init; }
}
