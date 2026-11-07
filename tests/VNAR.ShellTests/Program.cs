using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using LocaleGameHub.Models;
using LocaleGameHub.Services;

namespace VNAR.ShellTests;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // When VNAR launches a game during the modern-shortcut test, this executable
        // plays the part of LEProc.exe.
        if (args.Length > 0 && args[0] == ModernShortcutTests.FakeLeProcFlag)
            return ModernShortcutTests.RunFakeLeProc(args);
        if (args.Length > 0 && args[0] == GameWindowIdentityTests.FakeGameWindowFlag)
            return GameWindowIdentityTests.RunFakeGameWindow(args);

        var root = Path.Combine(Path.GetTempPath(), "VNAR Shell Tests " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        // Keep every VNAR data file in the temp folder (inherited by the VNAR.exe the
        // modern-shortcut test starts), never in the user's real library.
        Environment.SetEnvironmentVariable(AppDataPaths.DataDirOverrideVariable, Path.Combine(root, "data"));

        try
        {
            var first = new GameEntry { Name = "Normal game", RunAsAdmin = false };
            var second = new GameEntry { Name = "Admin game", RunAsAdmin = true };
            var icon = Path.Combine(root, "selected icon.ico");
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixture.ico"), icon);

            // Exercise real shell32 APIs, including resetting an incoming game ID
            // to the launcher ID. This does not launch games or request elevation.
            TaskbarIdentityService.SetCurrentProcessIdentity(first.Id);
            Equal(TaskbarIdentityService.GetGameAppId(first.Id), ReadProcessIdentity(), "first game process");
            TaskbarIdentityService.SetCurrentProcessIdentity();
            Equal(TaskbarIdentityService.LauncherAppId, ReadProcessIdentity(), "launcher after first game");
            TaskbarIdentityService.SetCurrentProcessIdentity(second.Id);
            Equal(TaskbarIdentityService.GetGameAppId(second.Id), ReadProcessIdentity(), "second game process");
            TaskbarIdentityService.SetCurrentProcessIdentity();
            Equal(TaskbarIdentityService.LauncherAppId, ReadProcessIdentity(), "launcher after second game");
            Console.WriteLine("PASS: explicit process identities remain separate.");

            var normalLink = ShortcutService.CreateShortcut(first, "Normal shortcut", root, icon);
            var adminLink = ShortcutService.CreateShortcut(second, "Admin shortcut", root, icon);
            VerifyShortcut(normalLink, first, icon);
            VerifyShortcut(adminLink, second, icon);
            NotEqual(ReadShortcutIdentity(normalLink), ReadShortcutIdentity(adminLink), "different games");
            NotEqual(TaskbarIdentityService.LauncherAppId, ReadShortcutIdentity(normalLink), "game vs launcher");
            Equal(TaskbarIdentityService.LauncherAppId, ReadProcessIdentity(), "creating links cannot change the UI identity");
            Console.WriteLine("PASS: normal/admin game shortcuts persist distinct AppIDs without changing the process identity.");

            // Updating a game's admin preference or display name must not change
            // the shortcut's grouping identity or turn it into the launcher.
            first.RunAsAdmin = true;
            first.Name = "Renamed game";
            var renamedLink = ShortcutService.CreateShortcut(first, "Renamed admin", root, icon);
            VerifyShortcut(renamedLink, first, icon);
            Equal(ReadShortcutIdentity(normalLink), ReadShortcutIdentity(renamedLink), "same game after rename/admin change");
            Equal(TaskbarIdentityService.LauncherAppId, ReadProcessIdentity(), "identity after later shortcuts");
            Console.WriteLine("PASS: game identity stays stable across rename/admin setting changes.");

            // An older link can be recreated in place, adding the property while
            // retaining the intended launch arguments and chosen icon.
            var replacement = ShortcutService.CreateShortcut(first, "Normal shortcut", root, icon);
            VerifyShortcut(replacement, first, icon);
            Console.WriteLine("PASS: shortcut replacement preserves target, arguments, description and icon.");

            // Games whose .exe (and therefore the chosen icon) has Japanese characters:
            // the link must keep the exact Unicode path, not an ANSI "???.exe" that
            // makes Explorer fall back to VNAR.exe's icon.
            var japaneseFolder = Path.Combine(root, "ゲーム フォルダ");
            Directory.CreateDirectory(japaneseFolder);
            var japaneseIcon = Path.Combine(japaneseFolder, "蒼の彼方のフォーリズム.ico");
            File.Copy(icon, japaneseIcon);
            var japaneseGame = new GameEntry { Name = "蒼の彼方のフォーリズム" };
            var japaneseLink = ShortcutService.CreateShortcut(japaneseGame, japaneseGame.Name, japaneseFolder, japaneseIcon);
            Equal(Path.Combine(japaneseFolder, "蒼の彼方のフォーリズム.lnk"), japaneseLink, "Unicode shortcut path");
            VerifyShortcut(japaneseLink, japaneseGame, japaneseIcon);
            Console.WriteLine("PASS: classic shortcuts keep Japanese names and icon paths intact.");

            ModernShortcutTests.Run(root, icon);
            GameWindowIdentityTests.Run(root, icon);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { /* a just-exited process may still hold a file; temp is cleaned by Windows */ }
        }
    }

    private static void VerifyShortcut(string path, GameEntry game, string icon)
    {
        Equal(TaskbarIdentityService.GetGameAppId(game.Id), ReadShortcutIdentity(path), "persisted AppUserModelID");

        // Read through IShellLinkW: WScript.Shell would turn non-ANSI characters into "?".
        var link = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"), true)!)!;
        try
        {
            ((System.Runtime.InteropServices.ComTypes.IPersistFile)link).Load(path, 0);
            var shellLink = (IShellLinkWReader)link;
            var buffer = new StringBuilder(1024);

            Marshal.ThrowExceptionForHR(shellLink.GetPath(buffer, buffer.Capacity, IntPtr.Zero, 0));
            Equal(ShortcutService.ResolveVnarExecutable().ToUpperInvariant(), buffer.ToString().ToUpperInvariant(), "shortcut target");

            buffer.Clear();
            Marshal.ThrowExceptionForHR(shellLink.GetArguments(buffer, buffer.Capacity));
            Equal($"--launch {game.Id:D}", buffer.ToString(), "shortcut arguments");

            buffer.Clear();
            Marshal.ThrowExceptionForHR(shellLink.GetIconLocation(buffer, buffer.Capacity, out var iconIndex));
            Equal(icon.ToUpperInvariant() + ",0", buffer.ToString().ToUpperInvariant() + "," + iconIndex, "chosen icon");

            buffer.Clear();
            Marshal.ThrowExceptionForHR(shellLink.GetDescription(buffer, buffer.Capacity));
            Equal($"VNAR · {game.Name}", buffer.ToString(), "shortcut description");
        }
        finally
        {
            Release(link);
        }
    }

    // Vtable order of IShellLinkW up to GetIconLocation; only getters are used here.
    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkWReader
    {
        [PreserveSig] int GetPath([MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int maxPath, IntPtr findData, uint flags);
        [PreserveSig] int GetIDList(out IntPtr idList);
        [PreserveSig] int SetIDList(IntPtr idList);
        [PreserveSig] int GetDescription([MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int maxName);
        [PreserveSig] int SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int GetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int maxPath);
        [PreserveSig] int SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        [PreserveSig] int GetArguments([MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int maxPath);
        [PreserveSig] int SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        [PreserveSig] int GetHotkey(out short hotkey);
        [PreserveSig] int SetHotkey(short hotkey);
        [PreserveSig] int GetShowCmd(out int showCmd);
        [PreserveSig] int SetShowCmd(int showCmd);
        [PreserveSig] int GetIconLocation([MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int maxIconPath, out int iconIndex);
    }

    private static string ReadShortcutIdentity(string path)
    {
        // Read through Explorer's canonical property name, independently of the
        // IPropertyStore implementation used to write the link.
        object? shellObject = null;
        object? folderObject = null;
        object? itemObject = null;
        try
        {
            shellObject = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);
            dynamic shell = shellObject!;
            folderObject = shell.NameSpace(Path.GetDirectoryName(path)!);
            dynamic folder = folderObject!;
            itemObject = folder.ParseName(Path.GetFileName(path));
            dynamic item = itemObject!;
            return Convert.ToString(item.ExtendedProperty("System.AppUserModel.ID")) ?? string.Empty;
        }
        finally
        {
            Release(itemObject);
            Release(folderObject);
            Release(shellObject);
        }
    }

    private static string ReadProcessIdentity()
    {
        Marshal.ThrowExceptionForHR(GetCurrentProcessExplicitAppUserModelID(out var pointer));
        try { return Marshal.PtrToStringUni(pointer) ?? string.Empty; }
        finally { Marshal.FreeCoTaskMem(pointer); }
    }

    private static void Equal(string expected, string actual, string label)
    {
        if (actual != expected)
            throw new InvalidOperationException($"{label}: expected '{expected}', got '{actual}'.");
    }

    private static void NotEqual(string first, string second, string label)
    {
        if (first == second)
            throw new InvalidOperationException($"{label}: unexpected shared identity '{first}'.");
    }

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
            Marshal.FinalReleaseComObject(value);
    }

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern int GetCurrentProcessExplicitAppUserModelID(out IntPtr appId);
}
