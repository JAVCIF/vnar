using Microsoft.Win32;

namespace LocaleGameHub.Services;

/// <summary>
/// Registers vnar:// for the current user (HKCU, no administrator rights), pointing
/// at this VNAR.exe. Registration happens only when a modern shortcut is created;
/// afterwards VNAR refreshes the path at startup in case a portable copy was moved.
/// Users who only create classic shortcuts never get a registry entry.
/// </summary>
public static class ProtocolRegistrationService
{
    private const string KeyPath = @"Software\Classes\" + VnarUri.Scheme;

    public static void Register(string vnarExePath)
    {
        using var root = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
        root.SetValue(string.Empty, "URL:VNAR");
        root.SetValue("URL Protocol", string.Empty);

        using (var icon = root.CreateSubKey("DefaultIcon", writable: true))
            icon.SetValue(string.Empty, $"\"{vnarExePath}\",0");

        using var command = root.CreateSubKey(@"shell\open\command", writable: true);
        command.SetValue(string.Empty, BuildCommand(vnarExePath));
    }

    /// <summary>Re-points an existing registration at <paramref name="vnarExePath"/> if it moved.</summary>
    public static void RefreshIfRegistered(string vnarExePath)
    {
        using var command = Registry.CurrentUser.OpenSubKey(KeyPath + @"\shell\open\command", writable: false);
        if (command is null) return; // never registered: leave the registry alone

        var current = command.GetValue(string.Empty) as string;
        if (!string.Equals(current, BuildCommand(vnarExePath), StringComparison.OrdinalIgnoreCase))
            Register(vnarExePath);
    }

    private static string BuildCommand(string vnarExePath) => $"\"{vnarExePath}\" \"%1\"";
}
