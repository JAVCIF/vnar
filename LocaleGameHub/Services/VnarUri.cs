namespace LocaleGameHub.Services;

/// <summary>
/// The "vnar://" protocol used by modern shortcuts, like steam:// or playnite://.
/// Only one command exists: vnar://launch/&lt;game id&gt;. Anything else is rejected,
/// because any website can ask the browser to open a vnar:// link.
/// No WPF dependencies.
/// </summary>
public static class VnarUri
{
    public const string Scheme = "vnar";
    private const string LaunchCommand = "launch";

    public static string BuildLaunch(Guid gameId) => $"{Scheme}://{LaunchCommand}/{gameId:D}";

    public static bool IsVnarUri(string? value)
        => Normalize(value).StartsWith(Scheme + ":", StringComparison.OrdinalIgnoreCase);

    public static bool TryParseLaunch(string? value, out Guid gameId)
    {
        gameId = Guid.Empty;
        var text = Normalize(value);
        if (!IsVnarUri(text) || !Uri.TryCreate(text, UriKind.Absolute, out var uri))
            return false;

        if (!uri.Scheme.Equals(Scheme, StringComparison.OrdinalIgnoreCase) ||
            !uri.Host.Equals(LaunchCommand, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
            return false;

        var id = uri.AbsolutePath.Trim('/');
        return Guid.TryParse(id, out gameId) && gameId != Guid.Empty;
    }

    // Command-line arguments may arrive wrapped in quotes or with stray spaces.
    private static string Normalize(string? value) => (value ?? string.Empty).Trim().Trim('"').Trim();
}
