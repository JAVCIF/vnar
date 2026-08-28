using System.Text.Json;
using LocaleGameHub.Models;

namespace LocaleGameHub.Services;

public sealed class LibraryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public List<GameEntry> Games { get; private set; } = [];
    public List<DeveloperEntry> Developers { get; private set; } = [];
    public AppSettings Settings { get; private set; } = new();

    public LibraryService()
    {
        AppDataPaths.Ensure();
        Load();
    }

    public void Load()
    {
        try
        {
            if (File.Exists(AppDataPaths.LibraryFile))
                Games = JsonSerializer.Deserialize<List<GameEntry>>(File.ReadAllText(AppDataPaths.LibraryFile), JsonOptions) ?? [];
        }
        catch
        {
            Games = [];
        }

        foreach (var game in Games)
            game.Developers ??= [];

        try
        {
            if (File.Exists(AppDataPaths.DevelopersFile))
                Developers = JsonSerializer.Deserialize<List<DeveloperEntry>>(File.ReadAllText(AppDataPaths.DevelopersFile), JsonOptions) ?? [];
        }
        catch
        {
            Developers = [];
        }

        try
        {
            if (File.Exists(AppDataPaths.SettingsFile))
                Settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppDataPaths.SettingsFile), JsonOptions) ?? new();
        }
        catch
        {
            Settings = new();
        }

        NormalizeSettings();
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

        if (changed) SaveSettings();
    }

    public void SaveGames()
    {
        AppDataPaths.Ensure();
        File.WriteAllText(AppDataPaths.LibraryFile, JsonSerializer.Serialize(Games, JsonOptions));
    }


    public void SaveDevelopers()
    {
        AppDataPaths.Ensure();
        File.WriteAllText(AppDataPaths.DevelopersFile, JsonSerializer.Serialize(Developers, JsonOptions));
    }

    public DeveloperEntry EnsureDeveloper(string vndbId, string name)
    {
        var existing = Developers.FirstOrDefault(d => string.Equals(d.VndbId, vndbId, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            existing = new DeveloperEntry { VndbId = vndbId, Name = name };
            Developers.Add(existing);
        }
        else if (!string.IsNullOrWhiteSpace(name) && !string.Equals(existing.Name, name, StringComparison.Ordinal))
        {
            existing.Name = name;
        }
        return existing;
    }

    public void SaveSettings()
    {
        AppDataPaths.Ensure();
        File.WriteAllText(AppDataPaths.SettingsFile, JsonSerializer.Serialize(Settings, JsonOptions));
    }

    public bool ContainsExe(string exePath) => Games.Any(g =>
        string.Equals(Path.GetFullPath(g.ExePath), Path.GetFullPath(exePath), StringComparison.OrdinalIgnoreCase));

    public void Add(GameEntry game)
    {
        if (!ContainsExe(game.ExePath))
        {
            Games.Add(game);
            SaveGames();
        }
    }

    public void Remove(GameEntry game)
    {
        Games.RemoveAll(g => g.Id == game.Id);
        SaveGames();
    }
}
