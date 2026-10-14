using System.Globalization;

namespace LocaleGameHub.Services;

/// <summary>What a library entry (game or developer) still uses from the covers folder.</summary>
/// <param name="OwnerId">Game Id or developer LocalId: the prefix of its cover file names.</param>
/// <param name="ReferencedPaths">Current cover and editor source paths.</param>
/// <param name="HasEditSource">
/// True when the entry records its editor source image. When false the cover editor
/// falls back to searching the folder for this owner's older originals, so those are kept.
/// </param>
public sealed record CoverOwner(Guid OwnerId, IReadOnlyCollection<string> ReferencedPaths, bool HasEditSource);

public sealed record CoverCleanupResult(int MovedToTrash, int PurgedFromTrash);

/// <summary>
/// Reclaims space in the covers folder without risking images that are still needed.
/// Unused files are first moved to "covers/_trash/&lt;date&gt;" and only deleted after
/// <see cref="TrashRetention"/>, so anything removed by mistake can be restored by hand.
/// No WPF dependencies.
/// </summary>
public static class CoverCleanupService
{
    public const string TrashFolderName = "_trash";

    /// <summary>Files newer than this are never touched (an editor may have just created them).</summary>
    public static readonly TimeSpan MinimumAge = TimeSpan.FromHours(24);

    public static readonly TimeSpan TrashRetention = TimeSpan.FromDays(30);

    public static CoverCleanupResult Run(string coversDir, IReadOnlyCollection<CoverOwner> owners, DateTime nowUtc)
        => RunCore(coversDir, owners, nowUtc, orphansOnly: false);

    /// <summary>
    /// Trashes only files whose owner no longer exists (same age, naming and trash
    /// rules as <see cref="Run"/>). Used for shortcut icons: VNAR cannot know which
    /// shortcuts outside the app still use an icon of an existing game, but a removed
    /// game's shortcuts no longer work, so its icons are safe to retire.
    /// </summary>
    public static CoverCleanupResult RunOrphansOnly(string folder, IReadOnlyCollection<Guid> ownerIds, DateTime nowUtc)
        => RunCore(folder, ownerIds.Select(id => new CoverOwner(id, [], HasEditSource: false)).ToList(), nowUtc, orphansOnly: true);

    private static CoverCleanupResult RunCore(string coversDir, IReadOnlyCollection<CoverOwner> owners, DateTime nowUtc, bool orphansOnly)
    {
        if (!Directory.Exists(coversDir)) return new CoverCleanupResult(0, 0);

        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var owner in owners)
            foreach (var path in owner.ReferencedPaths)
                if (TryFullPath(path, out var full)) referenced.Add(full);

        var byId = owners.GroupBy(o => o.OwnerId).ToDictionary(g => g.Key, g => g.First());
        var trashDir = Path.Combine(coversDir, TrashFolderName, nowUtc.ToLocalTime().ToString("yyyyMMdd", CultureInfo.InvariantCulture));
        var moved = 0;

        foreach (var file in SafeEnumerateFiles(coversDir))
        {
            if (!ShouldTrash(file, referenced, byId, nowUtc, orphansOnly)) continue;
            try
            {
                Directory.CreateDirectory(trashDir);
                var target = Path.Combine(trashDir, Path.GetFileName(file));
                if (File.Exists(target))
                    target = Path.Combine(trashDir, $"{Path.GetFileNameWithoutExtension(file)}_{Guid.NewGuid():N}{Path.GetExtension(file)}");
                File.Move(file, target);
                moved++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // In use or protected: try again on a later start.
            }
        }

        return new CoverCleanupResult(moved, PurgeTrash(Path.Combine(coversDir, TrashFolderName), nowUtc));
    }

    private static bool ShouldTrash(
        string file,
        HashSet<string> referenced,
        Dictionary<Guid, CoverOwner> owners,
        DateTime nowUtc,
        bool orphansOnly)
    {
        if (!TryFullPath(file, out var full) || referenced.Contains(full)) return false;

        DateTime lastWrite;
        try { lastWrite = File.GetLastWriteTimeUtc(full); }
        catch { return false; }
        if (nowUtc - lastWrite < MinimumAge) return false;

        var name = Path.GetFileName(full);

        // Interrupted saves from the cover editor (CoverService.SaveBitmapCover).
        if (name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) return true;

        // VNAR names covers "<owner id:N>_<kind>_<unique>.<ext>". Leave anything else alone.
        if (!TryParseOwner(name, out var ownerId)) return false;

        // The game or developer no longer exists: nothing can reach this file.
        if (!owners.TryGetValue(ownerId, out var owner)) return true;
        if (orphansOnly) return false;

        // Old renders are reproducible from the source image and edit settings.
        if (IsRendered(name)) return true;

        // An unused original is only needed as the editor's fallback source.
        return owner.HasEditSource;
    }

    /// <summary>Same rule as CoverService.LooksLikeRenderedCover.</summary>
    private static bool IsRendered(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        return stem.Contains("_edited", StringComparison.OrdinalIgnoreCase)
            || stem.Contains("_rendered", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseOwner(string fileName, out Guid ownerId)
    {
        ownerId = Guid.Empty;
        var underscore = fileName.IndexOf('_');
        return underscore == 32 && Guid.TryParseExact(fileName[..32], "N", out ownerId);
    }

    private static int PurgeTrash(string trashRoot, DateTime nowUtc)
    {
        if (!Directory.Exists(trashRoot)) return 0;
        var purged = 0;
        foreach (var dayDir in SafeEnumerateDirectories(trashRoot))
        {
            if (!DateTime.TryParseExact(Path.GetFileName(dayDir), "yyyyMMdd", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal, out var day))
                continue;
            if (nowUtc - day.ToUniversalTime() < TrashRetention) continue;
            try
            {
                purged += Directory.GetFiles(dayDir).Length;
                Directory.Delete(dayDir, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Try again next time.
            }
        }
        return purged;
    }

    private static IEnumerable<string> SafeEnumerateFiles(string dir)
    {
        try { return Directory.GetFiles(dir, "*", SearchOption.TopDirectoryOnly); }
        catch { return []; }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string dir)
    {
        try { return Directory.GetDirectories(dir); }
        catch { return []; }
    }

    private static bool TryFullPath(string? path, out string full)
    {
        full = string.Empty;
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            full = Path.GetFullPath(path);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
