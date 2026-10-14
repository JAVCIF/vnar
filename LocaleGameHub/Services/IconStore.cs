using System.Security.Cryptography;

namespace LocaleGameHub.Services;

/// <summary>A stored icon and whether this call created the file.</summary>
public readonly record struct StoredIcon(string Path, bool Created);

/// <summary>
/// Shortcut icons kept in VNAR's data folder (%LOCALAPPDATA%\VNAR\icons), so
/// shortcuts keep their icon when the original file or a portable VNAR folder moves.
/// No WPF dependencies.
/// </summary>
public static class IconStore
{
    /// <summary>Stable copy of VNAR's generic shortcut icon. Not GUID-prefixed, so cleanup ignores it.</summary>
    public const string GenericIconFileName = "vnar-generic.ico";

    /// <summary>
    /// Copies a user-chosen icon as "&lt;game id&gt;_&lt;content hash&gt;.ico". The same icon
    /// for the same game always maps to the same file, so re-creating or replacing a
    /// shortcut never piles up copies. Created is false when the file already existed.
    /// </summary>
    public static StoredIcon StoreCustomIcon(string icoPath, Guid gameId)
    {
        var bytes = File.ReadAllBytes(icoPath);
        var hash = Convert.ToHexString(SHA256.HashData(bytes))[..16].ToLowerInvariant();
        var target = Path.Combine(AppDataPaths.IconsDir, $"{gameId:N}_{hash}.ico");
        if (File.Exists(target)) return new StoredIcon(target, Created: false);

        WriteReplacing(target, bytes);
        return new StoredIcon(target, Created: true);
    }

    /// <summary>Removes an icon this session just stored, e.g. when creating the shortcut failed.</summary>
    public static void DiscardIfCreated(StoredIcon icon)
    {
        if (!icon.Created) return;
        try { File.Delete(icon.Path); } catch { /* cleaned up later with the game's other icons */ }
    }

    /// <summary>
    /// Returns a copy of VNAR's generic .ico in the data folder (refreshed when VNAR
    /// ships a different one). Falls back to <paramref name="sourceIco"/> if it is not an
    /// .ico file or cannot be copied.
    /// </summary>
    public static string EnsureGenericIconCopy(string sourceIco)
    {
        try
        {
            if (!sourceIco.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) || !File.Exists(sourceIco))
                return sourceIco;

            var target = Path.Combine(AppDataPaths.IconsDir, GenericIconFileName);
            var source = File.ReadAllBytes(sourceIco);
            if (File.Exists(target) && File.ReadAllBytes(target).AsSpan().SequenceEqual(source))
                return target;

            WriteReplacing(target, source);
            return target;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return sourceIco;
        }
    }

    /// <summary>Temp file + rename: Explorer never reads a half-written icon. No .bak kept.</summary>
    private static void WriteReplacing(string target, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temp = target + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, target, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }
}
