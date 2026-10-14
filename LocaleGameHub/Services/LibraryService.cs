using System.Text.Json;
using LocaleGameHub.Models;

namespace LocaleGameHub.Services;

public sealed class LibraryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    // Hash of library.json as this instance last read or wrote it. If the file on
    // disk differs at save time, another VNAR process (a game shortcut) wrote it.
    private string _gamesHash = string.Empty;

    public List<GameEntry> Games { get; private set; } = [];
    public List<DeveloperEntry> Developers { get; private set; } = [];
    public AppSettings Settings { get; private set; } = new();

    /// <summary>
    /// True when library.json existed at load time. An empty library from an existing
    /// file means the user removed every game; a missing file means a fresh install
    /// (or a file deleted by hand), where nothing should be treated as unused.
    /// </summary>
    public bool LibraryFileExisted { get; private set; }

    public LibraryService()
    {
        AppDataPaths.Ensure();
        Load();
    }

    public void Load()
    {
        JsonStore.WithDataLock(() =>
        {
            LibraryFileExisted = File.Exists(AppDataPaths.LibraryFile);
            Games = JsonStore.Load<List<GameEntry>>(AppDataPaths.LibraryFile, JsonOptions) ?? [];
            _gamesHash = JsonStore.HashFileOrEmpty(AppDataPaths.LibraryFile);
            Developers = JsonStore.Load<List<DeveloperEntry>>(AppDataPaths.DevelopersFile, JsonOptions) ?? [];
            Settings = JsonStore.Load<AppSettings>(AppDataPaths.SettingsFile, JsonOptions) ?? new();
        });

        NormalizeGames(Games);
        NormalizeSettings();
    }

    private static void NormalizeGames(List<GameEntry> games)
    {
        games.RemoveAll(g => g is null);
        foreach (var game in games)
            game.Developers ??= [];
    }

    private void NormalizeSettings()
    {
        var changed = false;
        if (!string.Equals(Settings.Language, "es", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Settings.Language, "en", StringComparison.OrdinalIgnoreCase))
        {
            Settings.Language = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
                .Equals("es", StringComparison.OrdinalIgnoreCase) ? "es" : "en";
            changed = true;
        }

        if (Settings.PageSize < 10 || Settings.PageSize > 50)
        {
            Settings.PageSize = 20;
            changed = true;
        }

        var migratedSecret = false;
        if (!string.IsNullOrWhiteSpace(Settings.LegacySerpApiKey))
        {
            // Beta 1.2 and earlier stored the SerpApi key in plain text: encrypt it.
            try
            {
                if (string.IsNullOrEmpty(Settings.SerpApiKeyProtected))
                    Settings.SerpApiKey = Settings.LegacySerpApiKey;
                Settings.LegacySerpApiKey = null;
                changed = migratedSecret = true;
            }
            catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or PlatformNotSupportedException)
            {
                // Encryption unavailable: keep the old value rather than lose the key
                // or block startup. The migration is retried on the next start.
            }
        }
        else if (Settings.LegacySerpApiKey is not null)
        {
            Settings.LegacySerpApiKey = null; // empty legacy field: just drop it
            changed = true;
        }

        if (!changed) return;
        SaveSettings();
        // Each save keeps the previous file as settings.json.bak, which would still hold
        // the plain-text key. A second save rotates that copy out too.
        if (migratedSecret) SaveSettings();
    }

    public void SaveGames()
    {
        AppDataPaths.Ensure();
        JsonStore.WithDataLock(() =>
        {
            MergeExternalChanges();
            var written = JsonStore.Save(AppDataPaths.LibraryFile, Games, JsonOptions);
            _gamesHash = JsonStore.Hash(written);
        });
    }

    /// <summary>
    /// Records that a game was launched, reading the library fresh from disk first.
    /// Used by short-lived shortcut processes so they never overwrite edits made in
    /// the library window with the snapshot they loaded at startup.
    /// </summary>
    public void RecordLaunch(Guid gameId, DateTime launchedUtc)
    {
        AppDataPaths.Ensure();
        JsonStore.WithDataLock(() =>
        {
            if (JsonStore.TryReadQuietly<List<GameEntry>>(AppDataPaths.LibraryFile, JsonOptions, out var fresh, out _) &&
                fresh is not null)
            {
                NormalizeGames(fresh);
                Games = fresh;
            }

            var game = Games.FirstOrDefault(g => g.Id == gameId);
            if (game is null) return;
            game.LastPlayedUtc = launchedUtc;

            var written = JsonStore.Save(AppDataPaths.LibraryFile, Games, JsonOptions);
            _gamesHash = JsonStore.Hash(written);
        });
    }

    /// <summary>
    /// Another VNAR process only ever changes LastPlayedUtc (see RecordLaunch).
    /// Keep the newest value for each game so this save does not discard it.
    /// Must run inside the data lock.
    /// </summary>
    private void MergeExternalChanges()
    {
        if (!JsonStore.TryReadQuietly<List<GameEntry>>(AppDataPaths.LibraryFile, JsonOptions, out var onDisk, out var bytes) ||
            onDisk is null)
            return;

        if (string.Equals(JsonStore.Hash(bytes), _gamesHash, StringComparison.Ordinal))
            return;

        var diskById = onDisk.Where(g => g is not null).GroupBy(g => g.Id).ToDictionary(g => g.Key, g => g.First());
        foreach (var game in Games)
        {
            if (!diskById.TryGetValue(game.Id, out var disk) || disk.LastPlayedUtc is null) continue;
            if (game.LastPlayedUtc is null || disk.LastPlayedUtc > game.LastPlayedUtc)
                game.LastPlayedUtc = disk.LastPlayedUtc;
        }
    }

    // Set when EnsureDeveloper adds or renames an entry that has not been saved yet.
    private bool _developersDirty;

    public void SaveDevelopers()
    {
        AppDataPaths.Ensure();
        JsonStore.WithDataLock(() => JsonStore.Save(AppDataPaths.DevelopersFile, Developers, JsonOptions));
        _developersDirty = false;
    }

    /// <summary>Saves developers.json only if EnsureDeveloper changed something.</summary>
    public void SaveDevelopersIfDirty()
    {
        if (_developersDirty) SaveDevelopers();
    }

    public DeveloperEntry EnsureDeveloper(string vndbId, string name)
    {
        var existing = Developers.FirstOrDefault(d => string.Equals(d.VndbId, vndbId, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            existing = new DeveloperEntry { VndbId = vndbId, Name = name };
            Developers.Add(existing);
            _developersDirty = true;
        }
        else if (!string.IsNullOrWhiteSpace(name) && !string.Equals(existing.Name, name, StringComparison.Ordinal))
        {
            existing.Name = name;
            _developersDirty = true;
        }
        return existing;
    }

    public void SaveSettings()
    {
        AppDataPaths.Ensure();
        JsonStore.WithDataLock(() => JsonStore.Save(AppDataPaths.SettingsFile, Settings, JsonOptions));
    }

    public bool ContainsExe(string exePath)
    {
        var target = NormalizeExePath(exePath);
        return Games.Any(g => string.Equals(NormalizeExePath(g.ExePath), target, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Snapshot of the library's executable paths for fast lookups (e.g. filtering
    /// hundreds of scan results). Safe to use from a worker thread.
    /// </summary>
    public HashSet<string> BuildExeIndex()
        => new(Games.Select(g => NormalizeExePath(g.ExePath)), StringComparer.OrdinalIgnoreCase);

    public static string NormalizeExePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // A malformed stored path must not break duplicate checks for the rest.
            return path.Trim();
        }
    }

    public void Add(GameEntry game) => AddRange([game]);

    /// <summary>
    /// Adds every game whose executable is not already in the library (duplicates
    /// within <paramref name="games"/> included) and saves once. Importing 400 games
    /// is one write, not 400. Returns the games actually added.
    /// </summary>
    public IReadOnlyList<GameEntry> AddRange(IEnumerable<GameEntry> games)
    {
        var known = BuildExeIndex();
        var added = new List<GameEntry>();
        foreach (var game in games)
        {
            if (!known.Add(NormalizeExePath(game.ExePath))) continue;
            Games.Add(game);
            added.Add(game);
        }

        if (added.Count > 0) SaveGames();
        return added;
    }

    public void Remove(GameEntry game)
    {
        Games.RemoveAll(g => g.Id == game.Id);
        SaveGames();
    }
}
