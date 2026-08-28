namespace LocaleGameHub.Services;

public static class AppDataPaths
{
    private static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static readonly string LegacyRoot = Path.Combine(LocalAppData, "LocaleGameHub");

    public static string Root { get; } = Path.Combine(LocalAppData, "VNAR");

    public static string LibraryFile => Path.Combine(Root, "library.json");
    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string DevelopersFile => Path.Combine(Root, "developers.json");
    public static string CoversDir => Path.Combine(Root, "covers");

    public static void Ensure()
    {
        MigrateLegacyDataIfNeeded();
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(CoversDir);
    }

    private static void MigrateLegacyDataIfNeeded()
    {
        if (Directory.Exists(Root) || !Directory.Exists(LegacyRoot)) return;

        DirectoryCopy(LegacyRoot, Root, true);
    }

    private static void DirectoryCopy(string sourceDir, string destDir, bool copySubDirs)
    {
        var dir = new DirectoryInfo(sourceDir);
        if (!dir.Exists) return;

        Directory.CreateDirectory(destDir);

        foreach (var file in dir.GetFiles())
        {
            var target = Path.Combine(destDir, file.Name);
            file.CopyTo(target, true);
        }

        if (!copySubDirs) return;

        foreach (var subDir in dir.GetDirectories())
        {
            var next = Path.Combine(destDir, subDir.Name);
            DirectoryCopy(subDir.FullName, next, true);
        }
    }
}
