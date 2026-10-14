using System.Windows.Media;

namespace LocaleGameHub.Models;

public sealed class ShortcutIconOption
{
    public required string DisplayName { get; init; }
    public required string IconPath { get; init; }
    public required ImageSource Preview { get; init; }
    public bool IsGeneric { get; init; }

    /// <summary>A .ico chosen by the user; copied into VNAR's data folder when used.</summary>
    public bool IsCustom { get; init; }
}
