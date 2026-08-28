using System;
using System.IO;
using System.Runtime.InteropServices;
using LocaleGameHub.Models;
using LocaleGameHub.Services;

namespace VNAR.ShellTests;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "VNAR Shell Tests " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

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

            var normalLink = ShortcutService.CreateShortcut(first, "Normal á", root, icon);
            var adminLink = ShortcutService.CreateShortcut(second, "Admin かな", root, icon);
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
            var replacement = ShortcutService.CreateShortcut(first, "Normal á", root, icon);
            VerifyShortcut(replacement, first, icon);
            Console.WriteLine("PASS: shortcut replacement preserves target, arguments, description and icon.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void VerifyShortcut(string path, GameEntry game, string icon)
    {
        Equal(TaskbarIdentityService.GetGameAppId(game.Id), ReadShortcutIdentity(path), "persisted AppUserModelID");

        object? shellObject = null;
        object? linkObject = null;
        try
        {
            shellObject = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
            dynamic shell = shellObject!;
            linkObject = shell.CreateShortcut(path);
            dynamic link = linkObject;
            Equal(ShortcutService.ResolveVnarExecutable().ToUpperInvariant(),
                ((string)link.TargetPath).ToUpperInvariant(), "shortcut target");
            Equal($"--launch {game.Id:D}", (string)link.Arguments, "shortcut arguments");
            Equal(icon.ToUpperInvariant() + ",0", ((string)link.IconLocation).ToUpperInvariant(), "chosen icon");
            Equal($"VNAR · {game.Name}", (string)link.Description, "shortcut description");
        }
        finally
        {
            Release(linkObject);
            Release(shellObject);
        }
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
