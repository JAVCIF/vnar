using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using LocaleGameHub.Models;
using Microsoft.Win32;

namespace LocaleGameHub.Services;

/// <summary>
/// Classic launches of games that run as administrator: VNAR must be elevated too,
/// or Windows will not let it give the game's windows the shortcut's identity.
/// The shortcut's VNAR relaunches itself elevated and that copy starts Locale
/// Emulator, so the user still sees a single UAC prompt (Locale Emulator does not
/// ask again when started from an elevated process).
/// </summary>
public static class ElevationService
{
    /// <summary>VNAR's data folder, passed to the elevated copy: elevation may run as another user.</summary>
    public const string DataDirArgument = "--data-dir";

    /// <summary>The icon of the shortcut that was used, which the elevated copy cannot see.</summary>
    public const string RelaunchIconArgument = "--relaunch-icon";

    /// <summary>The launch came from the library window, not from a shortcut.</summary>
    public const string FromLibraryArgument = "--from-library";

    private const int ErrorCancelled = 1223;

    public static bool GameRunsElevated(GameEntry game)
    {
        string? manifest = null;
        string? layers = null;
        try { manifest = ReadManifest(game.ExePath); } catch { /* unreadable exe: rely on the rest */ }
        try { layers = ReadCompatibilityLayers(game.ExePath); } catch { }

        return ElevationRules.GameRunsElevated(
            game.RunAsAdmin,
            ElevationRules.ManifestLevel(manifest),
            ElevationRules.CompatibilityLevel(layers),
            IsFilteredAdministrator());
    }

    public enum RelaunchResult { Started, Cancelled, Failed }

    /// <summary>Starts an elevated VNAR for the same classic launch.</summary>
    public static RelaunchResult RelaunchElevated(string vnarExe, Guid gameId, string relaunchIcon, bool fromLibrary)
    {
        var args = new StringBuilder()
            .Append("--launch ").Append(gameId.ToString("D"))
            .Append(' ').Append(DataDirArgument).Append(' ').Append(QuoteArgument(AppDataPaths.Root))
            .Append(' ').Append(RelaunchIconArgument).Append(' ').Append(QuoteArgument(relaunchIcon))
            .Append(fromLibrary ? " " + FromLibraryArgument : string.Empty)
            .ToString();
        try
        {
            using var process = Process.Start(new ProcessStartInfo(vnarExe, args)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(vnarExe) ?? AppContext.BaseDirectory
            });
            return process is null ? RelaunchResult.Failed : RelaunchResult.Started;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return RelaunchResult.Cancelled;
        }
        catch
        {
            return RelaunchResult.Failed;
        }
    }

    public static bool HasFlag(string[] args, string name)
        => args.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Value of "--name value" in the command line, or null.</summary>
    public static string? GetArgument(string[] args, string name)
    {
        for (var i = 0; i + 1 < args.Length; i++)
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }

    // Windows command-line quoting (CommandLineToArgvW rules), so a path ending in
    // a backslash or containing quotes reaches the elevated copy unchanged.
    private static string QuoteArgument(string value)
    {
        var sb = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var c in value)
        {
            if (c == '\\') { backslashes++; continue; }
            sb.Append('\\', c == '"' ? backslashes * 2 + 1 : backslashes);
            backslashes = 0;
            sb.Append(c);
        }
        sb.Append('\\', backslashes * 2).Append('"');
        return sb.ToString();
    }

    // ------------------------------------------------------------------ token

    public static bool IsCurrentProcessElevated()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        return TryGetTokenInt(identity.Token, TokenElevation, out var elevated) && elevated != 0;
    }

    /// <summary>An administrator whose current token is the filtered (UAC) one.</summary>
    private static bool IsFilteredAdministrator()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        return TryGetTokenInt(identity.Token, TokenElevationTypeClass, out var type) && type == TokenElevationTypeLimited;
    }

    private const int TokenElevationTypeClass = 18;
    private const int TokenElevation = 20;
    private const int TokenElevationTypeLimited = 3;

    private static bool TryGetTokenInt(IntPtr token, int infoClass, out int value)
    {
        var buffer = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            var ok = GetTokenInformation(token, infoClass, buffer, sizeof(int), out _);
            value = ok ? Marshal.ReadInt32(buffer) : 0;
            return ok;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    // --------------------------------------------------------------- manifest

    private const uint LoadLibraryAsDatafile = 0x2;
    private const uint LoadLibraryAsImageResource = 0x20;
    private static readonly IntPtr RtManifest = new(24);
    private static readonly IntPtr ProcessManifestId = new(1);

    private static string? ReadManifest(string exePath)
    {
        if (!File.Exists(exePath)) return null;
        var module = LoadLibraryExW(exePath, IntPtr.Zero, LoadLibraryAsDatafile | LoadLibraryAsImageResource);
        if (module == IntPtr.Zero) return null;
        try
        {
            var resource = FindResourceW(module, ProcessManifestId, RtManifest);
            if (resource == IntPtr.Zero) return null;
            var size = SizeofResource(module, resource);
            var loaded = LoadResource(module, resource);
            var data = loaded == IntPtr.Zero ? IntPtr.Zero : LockResource(loaded);
            if (data == IntPtr.Zero || size == 0) return null;

            var bytes = new byte[size];
            Marshal.Copy(data, bytes, 0, (int)size);
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            FreeLibrary(module);
        }
    }

    private const string LayersKey = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";

    private static string? ReadCompatibilityLayers(string exePath)
    {
        var path = Path.GetFullPath(exePath);
        using var user = Registry.CurrentUser.OpenSubKey(LayersKey);
        using var machine = Registry.LocalMachine.OpenSubKey(LayersKey);
        var values = new[] { user?.GetValue(path) as string, machine?.GetValue(path) as string }
            .Where(v => !string.IsNullOrWhiteSpace(v));
        var joined = string.Join(' ', values);
        return joined.Length == 0 ? null : joined;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr token, int infoClass, IntPtr info, int length, out int returned);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryExW(string fileName, IntPtr file, uint flags);

    [DllImport("kernel32.dll")]
    private static extern bool FreeLibrary(IntPtr module);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindResourceW(IntPtr module, IntPtr name, IntPtr type);

    [DllImport("kernel32.dll")]
    private static extern uint SizeofResource(IntPtr module, IntPtr resource);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LoadResource(IntPtr module, IntPtr resource);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LockResource(IntPtr resource);
}
