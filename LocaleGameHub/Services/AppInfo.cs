using System.Reflection;

namespace LocaleGameHub.Services;

/// <summary>Version information derived from Directory.Build.props.</summary>
public static class AppInfo
{
    public const string ProjectUrl = "https://github.com/JAVCIF/vnar";

    /// <summary>Version as written in Directory.Build.props, e.g. "1.0.0-beta.1.3".</summary>
    public static string Version { get; } = ReadVersion();

    /// <summary>Identifies VNAR to APIs (VNDB, GitHub, SerpApi).</summary>
    public static string UserAgent => $"VNAR/{Version} (+{ProjectUrl})";

    /// <summary>
    /// For plain image downloads: some image hosts reject requests that do not
    /// look like a browser, so keep a browser-style prefix and identify VNAR after it.
    /// </summary>
    public static string BrowserLikeUserAgent => $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) VNAR/{Version}";

    private static string ReadVersion()
    {
        var informational = typeof(AppInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            // Drop build metadata ("+abc123") in case it is ever re-enabled.
            var plus = informational.IndexOf('+');
            return plus > 0 ? informational[..plus] : informational;
        }

        return typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
