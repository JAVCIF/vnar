using System.Runtime.InteropServices;
using LocaleGameHub.Models;

namespace LocaleGameHub.Services;

public static class ShortcutService
{
    public static string GetGenericIconPath()
    {
        var outputIcon = Path.Combine(AppContext.BaseDirectory, "Resources", "VNARShortcut.ico");
        if (File.Exists(outputIcon)) return outputIcon;

        // During unusual publish layouts fall back to VNAR's application icon.
        return ResolveVnarExecutable();
    }

    public static string CreateShortcut(GameEntry game, string shortcutName, string destinationFolder, string iconPath)
    {
        if (string.IsNullOrWhiteSpace(shortcutName))
            throw new InvalidOperationException(LocalizationService.Bi("Escribe un nombre para el acceso directo.", "Enter a name for the shortcut."));
        if (!Directory.Exists(destinationFolder))
            throw new DirectoryNotFoundException(LocalizationService.Bi("La carpeta de destino no existe.", "The destination folder does not exist."));

        var safeName = SanitizeFileName(shortcutName.Trim());
        if (string.IsNullOrWhiteSpace(safeName))
            throw new InvalidOperationException(LocalizationService.Bi("El nombre del acceso directo no es válido.", "The shortcut name is not valid."));

        var shortcutPath = Path.Combine(destinationFolder, safeName + ".lnk");
        var target = ResolveVnarExecutable();
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException(LocalizationService.Bi("Windows Script Host no está disponible para crear el acceso directo.", "Windows Script Host is not available to create the shortcut."));

        object? shellObject = null;
        object? shortcutObject = null;
        try
        {
            shellObject = Activator.CreateInstance(shellType)
                ?? throw new InvalidOperationException(LocalizationService.Bi("No se pudo iniciar el creador de accesos directos de Windows.", "Could not start the Windows shortcut creator."));

            dynamic shell = shellObject;
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            shortcutObject = shortcut;

            shortcut.TargetPath = target;
            shortcut.Arguments = $"--launch {game.Id:D}";
            shortcut.WorkingDirectory = AppContext.BaseDirectory;
            shortcut.Description = $"VNAR · {game.Name}";
            shortcut.IconLocation = BuildIconLocation(iconPath);
            shortcut.Save();
        }
        finally
        {
            ReleaseCom(shortcutObject);
            ReleaseCom(shellObject);
        }

        TaskbarIdentityService.SetShortcutIdentity(shortcutPath, game.Id);
        return shortcutPath;
    }

    private static string BuildIconLocation(string iconPath)
    {
        if (string.IsNullOrWhiteSpace(iconPath) || !File.Exists(iconPath))
            iconPath = GetGenericIconPath();
        return iconPath + ",0";
    }

    public static string ResolveVnarExecutable()
    {
        var current = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(current) &&
            string.Equals(Path.GetFileName(current), "VNAR.exe", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(current))
            return current;

        var besideApp = Path.Combine(AppContext.BaseDirectory, "VNAR.exe");
        if (File.Exists(besideApp)) return besideApp;

        if (!string.IsNullOrWhiteSpace(current) && File.Exists(current)) return current;

        throw new FileNotFoundException(LocalizationService.Bi("No pude localizar VNAR.exe para crear el acceso directo.", "Could not locate VNAR.exe to create the shortcut."));
    }

    private static string SanitizeFileName(string value)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');
        return value.Trim().TrimEnd('.');
    }

    private static void ReleaseCom(object? value)
    {
        try
        {
            if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
        }
        catch { }
    }
}
