using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using LocaleGameHub.Models;
using LocaleGameHub.Services;

namespace VNAR.ShellTests;

/// <summary>
/// Classic launches of games started through a loader (the loader creates the real
/// game process and exits): VNAR must give the game's own window the shortcut's
/// AppUserModelID and relaunch command from outside that process.
/// </summary>
internal static class GameWindowIdentityTests
{
    /// <summary>This executable plays the game: it opens a window and writes its handle to a file.</summary>
    public const string FakeGameWindowFlag = "--fake-game-window";

    public static int RunFakeGameWindow(string[] args)
    {
        if (args.Length < 2) return 3;
        var window = new Window
        {
            Title = "VNAR fake game",
            Width = 240,
            Height = 120,
            ShowActivated = false,
            ShowInTaskbar = true
        };
        window.Loaded += (_, _) =>
            File.WriteAllText(args[1], new WindowInteropHelper(window).Handle.ToInt64().ToString());
        new Application().Run(window);
        return 0;
    }

    public static void Run(string root, string icon)
    {
        var self = Environment.ProcessPath!;
        var ready = Path.Combine(root, "fake-game-window.txt");
        using var game = Process.Start(new ProcessStartInfo(self, $"{FakeGameWindowFlag} \"{ready}\"") { UseShellExecute = false })
            ?? throw new InvalidOperationException("Could not start the fake game window.");
        try
        {
            var hwnd = WaitForWindow(ready, game);

            // The game "lives" next to this test executable; its window belongs to
            // another process, like main.bin started by a loader.
            var entry = new GameEntry { Name = "まじこいＡ－４", ExePath = Path.Combine(Path.GetDirectoryName(self)!, "まじこいＡ－４.exe") };
            var vnarExe = Path.Combine(root, "VNAR.exe");
            var watcher = new GameWindowIdentityService(entry, vnarExe, icon + ",0");

            var changed = watcher.TagNewWindows();
            if (changed < 1) throw new InvalidOperationException($"expected the game window to be tagged, {changed} changed.");

            Equal(TaskbarIdentityService.GetGameAppId(entry.Id),
                GameWindowIdentityService.ReadWindowString(hwnd, GameWindowIdentityService.AppIdPropertyId), "window AppUserModelID");
            Equal($"\"{vnarExe}\" --launch {entry.Id:D}",
                GameWindowIdentityService.ReadWindowString(hwnd, GameWindowIdentityService.RelaunchCommandPropertyId), "relaunch command");
            Equal(icon + ",0",
                GameWindowIdentityService.ReadWindowString(hwnd, GameWindowIdentityService.RelaunchIconPropertyId), "relaunch icon");
            Equal(entry.Name,
                GameWindowIdentityService.ReadWindowString(hwnd, GameWindowIdentityService.RelaunchNamePropertyId), "relaunch name");
            Equal(TaskbarIdentityService.LauncherAppId, ReadOwnProcessIdentity(), "tagging a game window does not change VNAR's identity");

            var again = watcher.TagNewWindows();
            if (again != 0) throw new InvalidOperationException($"already tagged windows were changed again ({again}).");
            Console.WriteLine("PASS: classic launches give a game window in another process the shortcut's AppID and relaunch command.");

            // A game in another folder must not claim this window.
            var other = new GameEntry { Name = "Other", ExePath = Path.Combine(root, "other", "game.exe") };
            Directory.CreateDirectory(Path.GetDirectoryName(other.ExePath)!);
            var otherWatcher = new GameWindowIdentityService(other, vnarExe, icon + ",0");
            if (otherWatcher.TagNewWindows() != 0) throw new InvalidOperationException("a window from another folder was tagged.");
            Equal(TaskbarIdentityService.GetGameAppId(entry.Id),
                GameWindowIdentityService.ReadWindowString(hwnd, GameWindowIdentityService.AppIdPropertyId), "window keeps its game's AppID");
            Console.WriteLine("PASS: windows of processes outside the game folder are left alone.");

            RunLibraryLaunch(root, self, entry, vnarExe, icon);
        }
        finally
        {
            try { game.Kill(); game.WaitForExit(5000); } catch { /* already gone */ }
        }
    }

    /// <summary>
    /// Launches from the library window: same AppID (so the game groups with a pinned
    /// classic shortcut) but not pinnable, and no relaunch command.
    /// </summary>
    private static void RunLibraryLaunch(string root, string self, GameEntry entry, string vnarExe, string icon)
    {
        var ready = Path.Combine(root, "fake-library-game-window.txt");
        using var game = Process.Start(new ProcessStartInfo(self, $"{FakeGameWindowFlag} \"{ready}\"") { UseShellExecute = false })
            ?? throw new InvalidOperationException("Could not start the second fake game window.");
        try
        {
            var hwnd = WaitForWindow(ready, game);
            var watcher = new GameWindowIdentityService(entry, vnarExe, icon + ",0", preventPinning: true);
            if (watcher.TagNewWindows() < 1) throw new InvalidOperationException("the library-launched game window was not tagged.");

            Equal(TaskbarIdentityService.GetGameAppId(entry.Id),
                GameWindowIdentityService.ReadWindowString(hwnd, GameWindowIdentityService.AppIdPropertyId), "library window AppUserModelID");
            if (!GameWindowIdentityService.ReadWindowPreventPinning(hwnd))
                throw new InvalidOperationException("library window: PreventPinning was not set.");
            Equal(null, GameWindowIdentityService.ReadWindowString(hwnd, GameWindowIdentityService.RelaunchCommandPropertyId), "library window relaunch command");
            Console.WriteLine("PASS: library launches share the game's AppID but cannot be pinned from the running game.");
        }
        finally
        {
            try { game.Kill(); game.WaitForExit(5000); } catch { /* already gone */ }
        }
    }

    private static IntPtr WaitForWindow(string readyFile, Process game)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(30))
        {
            if (game.HasExited) throw new InvalidOperationException($"the fake game exited early ({game.ExitCode}).");
            try
            {
                if (File.Exists(readyFile) && long.TryParse(File.ReadAllText(readyFile), out var handle) && handle != 0)
                    return new IntPtr(handle);
            }
            catch (IOException) { /* still being written */ }
            Thread.Sleep(100);
        }
        throw new TimeoutException("the fake game window did not appear.");
    }

    private static string ReadOwnProcessIdentity()
    {
        System.Runtime.InteropServices.Marshal.ThrowExceptionForHR(GetCurrentProcessExplicitAppUserModelID(out var pointer));
        try { return System.Runtime.InteropServices.Marshal.PtrToStringUni(pointer) ?? string.Empty; }
        finally { System.Runtime.InteropServices.Marshal.FreeCoTaskMem(pointer); }
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll", ExactSpelling = true)]
    private static extern int GetCurrentProcessExplicitAppUserModelID(out IntPtr appId);

    private static void Equal(string? expected, string? actual, string label)
    {
        if (actual != expected)
            throw new InvalidOperationException($"{label}: expected '{expected ?? "<null>"}', got '{actual ?? "<null>"}'.");
    }
}
