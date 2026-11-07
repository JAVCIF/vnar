using System.Text.RegularExpressions;

namespace LocaleGameHub.Services;

/// <summary>
/// Decides whether a game will run as administrator. Pure logic, so it can be tested
/// on any OS; <see cref="ElevationService"/> gathers the inputs on Windows.
/// </summary>
public static class ElevationRules
{
    public const string RequireAdministrator = "requireAdministrator";
    public const string HighestAvailable = "highestAvailable";
    public const string AsInvoker = "asInvoker";

    private static readonly Regex LevelPattern = new(
        "requestedExecutionLevel[^>]*?\\blevel\\s*=\\s*[\"']([A-Za-z]+)[\"']",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The requestedExecutionLevel of an application manifest, or null if it has none.</summary>
    public static string? ManifestLevel(string? manifestXml)
    {
        if (string.IsNullOrWhiteSpace(manifestXml)) return null;
        var match = LevelPattern.Match(manifestXml);
        if (!match.Success) return null;

        var level = match.Groups[1].Value;
        foreach (var known in new[] { RequireAdministrator, HighestAvailable, AsInvoker })
            if (level.Equals(known, StringComparison.OrdinalIgnoreCase)) return known;
        return null;
    }

    /// <summary>
    /// Reads Windows' compatibility layers for an executable (the "Run this program as
    /// an administrator" checkbox stores RUNASADMIN; RUNASHIGHEST also exists).
    /// Returns the matching manifest-style level, or null.
    /// </summary>
    public static string? CompatibilityLevel(string? layers)
    {
        if (string.IsNullOrWhiteSpace(layers)) return null;
        var flags = layers.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (flags.Any(f => f.Equals("RUNASADMIN", StringComparison.OrdinalIgnoreCase))) return RequireAdministrator;
        if (flags.Any(f => f.Equals("RUNASHIGHEST", StringComparison.OrdinalIgnoreCase))) return HighestAvailable;
        return null;
    }

    /// <param name="markedAdmin">The game uses VNAR's administrator Locale Emulator profile.</param>
    /// <param name="manifestLevel">From the exe's manifest (see <see cref="ManifestLevel"/>).</param>
    /// <param name="compatibilityLevel">From Windows' compatibility settings (see <see cref="CompatibilityLevel"/>).</param>
    /// <param name="userCanElevateSilently">
    /// The user is an administrator running with a filtered token: "highestAvailable"
    /// elevates for them, while for a standard user it runs as invoker.
    /// </param>
    public static bool GameRunsElevated(bool markedAdmin, string? manifestLevel, string? compatibilityLevel, bool userCanElevateSilently)
    {
        if (markedAdmin) return true;
        if (manifestLevel == RequireAdministrator || compatibilityLevel == RequireAdministrator) return true;
        if (manifestLevel == HighestAvailable || compatibilityLevel == HighestAvailable) return userCanElevateSilently;
        return false;
    }
}
