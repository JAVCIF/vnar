using System.Runtime.InteropServices;
using LocaleGameHub.Models;

namespace LocaleGameHub.Services;

public enum ShortcutKind
{
    /// <summary>.lnk → VNAR.exe --launch &lt;id&gt;. Can be pinned to the taskbar.</summary>
    Classic,

    /// <summary>
    /// .url → vnar://launch/&lt;id&gt;, like Steam or Playnite. No shortcut points at
    /// VNAR.exe, so Windows never shows a game's name or icon for VNAR itself.
    /// Windows does not allow pinning .url files to the taskbar.
    /// </summary>
    Modern
}

public static class ShortcutService
{
    public static string ExtensionFor(ShortcutKind kind) => kind == ShortcutKind.Modern ? ".url" : ".lnk";

    public static string CreateShortcut(GameEntry game, string shortcutName, string destinationFolder, string iconPath, ShortcutKind kind)
        => kind == ShortcutKind.Modern
            ? CreateUrlShortcut(game, shortcutName, destinationFolder, iconPath)
            : CreateShortcut(game, shortcutName, destinationFolder, iconPath);

    private static string CreateUrlShortcut(GameEntry game, string shortcutName, string destinationFolder, string iconPath)
    {
        var shortcutPath = BuildShortcutPath(shortcutName, destinationFolder, ".url");

        // The vnar:// protocol must exist before the shortcut is useful.
        ProtocolRegistrationService.Register(ResolveVnarExecutable());

        // The generic icon lives next to VNAR.exe; use a stable copy in the data folder
        // instead, so moving a portable VNAR does not leave modern shortcuts without
        // an icon (vnar:// itself is re-registered at startup).
        if (string.IsNullOrWhiteSpace(iconPath) || !File.Exists(iconPath) ||
            string.Equals(Path.GetFullPath(iconPath), Path.GetFullPath(GetGenericIconPath()), StringComparison.OrdinalIgnoreCase))
            iconPath = IconStore.EnsureGenericIconCopy(GetGenericIconPath());

        // Let Windows' own Internet Shortcut object write the file: it stores the URL
        // and the icon path correctly even when the path has Japanese characters,
        // which a hand-written ANSI .url file would not.
        var type = Type.GetTypeFromCLSID(new Guid("FBF23B40-E3F0-101B-8488-00AA003E56F8"), true)!;
        var shortcut = Activator.CreateInstance(type)
            ?? throw new InvalidOperationException(LocalizationService.Bi(
                "Windows no pudo crear el acceso directo moderno.", "Windows could not create the modern shortcut."));
        try
        {
            Marshal.ThrowExceptionForHR(((IUniformResourceLocatorW)shortcut).SetURL(VnarUri.BuildLaunch(game.Id), 0));
            Marshal.ThrowExceptionForHR(((IShellLinkW)shortcut).SetIconLocation(iconPath, 0));
            ((System.Runtime.InteropServices.ComTypes.IPersistFile)shortcut).Save(shortcutPath, true);
        }
        finally
        {
            ReleaseCom(shortcut);
        }

        return shortcutPath;
    }

    private static string BuildShortcutPath(string shortcutName, string destinationFolder, string extension)
    {
        if (string.IsNullOrWhiteSpace(shortcutName))
            throw new InvalidOperationException(LocalizationService.Bi("Escribe un nombre para el acceso directo.", "Enter a name for the shortcut."));
        if (!Directory.Exists(destinationFolder))
            throw new DirectoryNotFoundException(LocalizationService.Bi("La carpeta de destino no existe.", "The destination folder does not exist."));

        var safeName = SanitizeFileName(shortcutName.Trim());
        if (string.IsNullOrWhiteSpace(safeName))
            throw new InvalidOperationException(LocalizationService.Bi("El nombre del acceso directo no es válido.", "The shortcut name is not valid."));

        return Path.Combine(destinationFolder, safeName + extension);
    }

    [ComImport]
    [Guid("CABB0DA0-DA57-11CF-9974-0020AFD79762")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUniformResourceLocatorW
    {
        [PreserveSig] int SetURL([MarshalAs(UnmanagedType.LPWStr)] string url, uint flags);
        [PreserveSig] int GetURL(out IntPtr url);
        [PreserveSig] int InvokeCommand(IntPtr commandInfo);
    }

    // Full vtable order of IShellLinkW; only SetIconLocation is called.
    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        [PreserveSig] int GetPath(IntPtr file, int maxPath, IntPtr findData, uint flags);
        [PreserveSig] int GetIDList(out IntPtr idList);
        [PreserveSig] int SetIDList(IntPtr idList);
        [PreserveSig] int GetDescription(IntPtr name, int maxName);
        [PreserveSig] int SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int GetWorkingDirectory(IntPtr dir, int maxPath);
        [PreserveSig] int SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        [PreserveSig] int GetArguments(IntPtr args, int maxPath);
        [PreserveSig] int SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        [PreserveSig] int GetHotkey(out short hotkey);
        [PreserveSig] int SetHotkey(short hotkey);
        [PreserveSig] int GetShowCmd(out int showCmd);
        [PreserveSig] int SetShowCmd(int showCmd);
        [PreserveSig] int GetIconLocation(IntPtr iconPath, int maxIconPath, out int iconIndex);
        [PreserveSig] int SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        [PreserveSig] int SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, uint reserved);
        [PreserveSig] int Resolve(IntPtr hwnd, uint flags);
        [PreserveSig] int SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

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
