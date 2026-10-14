using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using LocaleGameHub.Models;
using LocaleGameHub.Services;
using Microsoft.Win32;

namespace VNAR.ShellTests;

/// <summary>
/// End-to-end check of modern (vnar://) shortcuts on a real Windows shell:
/// protocol registration, the .url written through COM (URL and icon), and a
/// double-click (ShellExecute of the .url) that starts the real VNAR.exe, which
/// launches the game through a fake LEProc and records the launch.
///
/// Safety: VNAR's data goes to a temp folder (VNAR_DATA_DIR, set in Program.Main),
/// and the user's HKCU\Software\Classes\vnar key is backed up and restored.
/// </summary>
internal static class ModernShortcutTests
{
    /// <summary>VNAR starts LEProc as: LEProc.exe -runas &lt;profile&gt; "&lt;game exe&gt;" &lt;game arguments&gt;.</summary>
    public const string FakeLeProcFlag = "-runas";

    private const string ProtocolKey = @"Software\Classes\" + VnarUri.Scheme;

    /// <summary>
    /// This test executable doubles as a fake LEProc: it writes the arguments it got
    /// to the file passed after "--out" (the game's arguments in the test library).
    /// </summary>
    public static int RunFakeLeProc(string[] args)
    {
        var i = Array.IndexOf(args, "--out");
        if (i < 0 || i + 1 >= args.Length) return 3;
        File.WriteAllText(args[i + 1], string.Join("\n", args));
        return 0;
    }

    public static void Run(string root, string customIcon)
    {
        var self = Environment.ProcessPath!;
        if (Path.GetFileNameWithoutExtension(self).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Run the shell tests through their executable (dotnet run), not 'dotnet VNAR.ShellTests.dll'.");

        var vnarExe = FindVnarExe();
        using var protocolBackup = RegistryBackup.Capture(ProtocolKey);

        // A test library in the temp data folder: one game whose "LEProc" is this
        // executable, with arguments telling the fake where to report.
        var marker = Path.Combine(root, "fake-leproc-args.txt");
        var profile = Guid.NewGuid().ToString();
        var library = new LibraryService();
        library.Settings.LocaleEmulatorPath = self;
        library.Settings.NormalProfileGuid = profile;
        library.Settings.AdminProfileGuid = Guid.NewGuid().ToString();
        library.SaveSettings();
        var game = new GameEntry { Name = "Modern game", ExePath = self, Arguments = $"--out \"{marker}\"" };
        library.AddRange([game]);

        // 1. Creating a modern shortcut registers vnar:// and writes the .url via COM.
        var url = ShortcutService.CreateShortcut(game, "Modern shortcut", root, customIcon, ShortcutKind.Modern);
        Equal(".url", Path.GetExtension(url), "modern shortcut extension");
        var (storedUrl, iconFile, iconIndex) = ReadUrlShortcut(url);
        Equal(VnarUri.BuildLaunch(game.Id), storedUrl, "stored URL");
        Equal(customIcon.ToUpperInvariant(), iconFile.ToUpperInvariant(), "stored icon");
        Equal("0", iconIndex.ToString(), "stored icon index");
        using (var key = Registry.CurrentUser.OpenSubKey(ProtocolKey))
        {
            Equal("URL:VNAR", key?.GetValue(string.Empty) as string ?? "<missing>", "protocol description");
            Equal(string.Empty, key?.GetValue("URL Protocol") as string ?? "<missing>", "URL Protocol marker");
        }
        Equal($"\"{ShortcutService.ResolveVnarExecutable()}\" \"%1\"", ReadProtocolCommand(), "registered command");
        Console.WriteLine("PASS: modern shortcut registers vnar:// and stores the launch URL and chosen icon.");

        // 2. The generic icon is taken from a stable copy in the data folder.
        var genericUrl = ShortcutService.CreateShortcut(game, "Modern generic", root, ShortcutService.GetGenericIconPath(), ShortcutKind.Modern);
        var expectedGeneric = ShortcutService.GetGenericIconPath().EndsWith(".ico", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(AppDataPaths.IconsDir, IconStore.GenericIconFileName)
            : ShortcutService.GetGenericIconPath();
        Equal(expectedGeneric.ToUpperInvariant(), ReadUrlShortcut(genericUrl).IconFile.ToUpperInvariant(), "generic icon location");
        Console.WriteLine("PASS: generic icon for modern shortcuts survives moving a portable VNAR.");

        // 3. A moved portable is re-registered at startup.
        ProtocolRegistrationService.Register(Path.Combine(root, "old location", "VNAR.exe"));
        ProtocolRegistrationService.RefreshIfRegistered(vnarExe);
        Equal($"\"{vnarExe}\" \"%1\"", ReadProtocolCommand(), "command after refresh");
        Console.WriteLine("PASS: startup refresh re-points vnar:// at the current VNAR.exe.");

        // 4. Double-click: ShellExecute the .url exactly as Explorer does.
        var started = DateTime.Now.AddSeconds(-2);
        using (Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })) { }

        var launchedArgs = WaitFor(() => File.Exists(marker) ? File.ReadAllText(marker) : null, TimeSpan.FromSeconds(60))
            ?? Fail<string>("the game was not launched (no fake LEProc call within 60 s)", vnarExe, started);
        var args = launchedArgs.Split('\n');
        Equal("-runas", args.ElementAtOrDefault(0) ?? "", "LEProc first argument");
        Equal(profile, args.ElementAtOrDefault(1) ?? "", "Locale Emulator profile");
        Equal(self.ToUpperInvariant(), (args.ElementAtOrDefault(2) ?? "").ToUpperInvariant(), "game executable");

        var lastPlayed = WaitFor(() => new LibraryService().Games.SingleOrDefault(g => g.Id == game.Id)?.LastPlayedUtc, TimeSpan.FromSeconds(30))
            ?? Fail<DateTime>("VNAR launched the game but did not record it as played", vnarExe, started);
        if (lastPlayed < DateTime.UtcNow.AddMinutes(-5)) throw new InvalidOperationException($"last played time is stale: {lastPlayed:O}");

        WaitForVnarToExit(vnarExe, started);
        Console.WriteLine("PASS: double-clicking the .url starts VNAR via vnar://, launches the game and records it.");
    }

    // ---------------------------------------------------------------- helpers

    private static string FindVnarExe()
    {
        // ShortcutService prefers a VNAR.exe next to the running program.
        var beside = Path.Combine(AppContext.BaseDirectory, "VNAR.exe");
        if (File.Exists(beside)) return beside;

        // Otherwise use the LocaleGameHub build output (built by the ProjectReference).
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var bin = Path.Combine(dir.FullName, "LocaleGameHub", "bin");
            if (!Directory.Exists(bin)) continue;
            var newest = Directory.EnumerateFiles(bin, "VNAR.exe", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (newest is not null) return newest;
        }

        throw new FileNotFoundException("VNAR.exe was not found. Build LocaleGameHub before running the shell tests.");
    }

    private static string ReadProtocolCommand()
    {
        using var command = Registry.CurrentUser.OpenSubKey(ProtocolKey + @"\shell\open\command");
        return command?.GetValue(string.Empty) as string ?? "<missing>";
    }

    private static (string Url, string IconFile, int IconIndex) ReadUrlShortcut(string path)
    {
        var type = Type.GetTypeFromCLSID(new Guid("FBF23B40-E3F0-101B-8488-00AA003E56F8"), true)!;
        var shortcut = Activator.CreateInstance(type)!;
        try
        {
            ((IPersistFile)shortcut).Load(path, 0);
            Marshal.ThrowExceptionForHR(((IUniformResourceLocatorW)shortcut).GetURL(out var urlPtr));
            string url;
            try { url = Marshal.PtrToStringUni(urlPtr) ?? string.Empty; }
            finally { Marshal.FreeCoTaskMem(urlPtr); }

            var icon = new StringBuilder(1024);
            Marshal.ThrowExceptionForHR(((IShellLinkW)shortcut).GetIconLocation(icon, icon.Capacity, out var index));
            return (url, icon.ToString(), index);
        }
        finally
        {
            Marshal.FinalReleaseComObject(shortcut);
        }
    }

    private static T? WaitFor<T>(Func<T?> probe, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var value = probe();
                if (value is not null) return value;
            }
            catch (IOException)
            {
                // File still being written by the other process.
            }
            Thread.Sleep(250);
        }
        return default;
    }

    private static IEnumerable<Process> TestVnarProcesses(string vnarExe, DateTime started)
    {
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(vnarExe)))
        {
            bool ours;
            try
            {
                ours = process.StartTime >= started &&
                       string.Equals(process.MainModule?.FileName, vnarExe, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                ours = false; // e.g. an elevated VNAR we cannot inspect: not ours
            }

            if (ours) yield return process;
            else process.Dispose();
        }
    }

    private static void WaitForVnarToExit(string vnarExe, DateTime started)
    {
        foreach (var process in TestVnarProcesses(vnarExe, started))
        {
            using (process)
            {
                if (process.WaitForExit(30_000)) continue;
                process.Kill();
                throw new InvalidOperationException("VNAR did not exit after launching (it may be showing an error dialog).");
            }
        }
    }

    private static T Fail<T>(string message, string vnarExe, DateTime started)
    {
        // Close a VNAR stuck on an error dialog so the run does not hang.
        foreach (var process in TestVnarProcesses(vnarExe, started))
        {
            using (process)
            {
                try { process.Kill(); } catch { }
            }
        }
        throw new InvalidOperationException(message);
    }

    private static void Equal(string expected, string actual, string label)
    {
        if (actual != expected)
            throw new InvalidOperationException($"{label}: expected '{expected}', got '{actual}'.");
    }

    /// <summary>Snapshot of a registry tree under HKCU, restored on Dispose.</summary>
    private sealed class RegistryBackup : IDisposable
    {
        private readonly string _path;
        private readonly Node? _snapshot;

        private RegistryBackup(string path, Node? snapshot)
        {
            _path = path;
            _snapshot = snapshot;
        }

        public static RegistryBackup Capture(string path)
        {
            using var key = Registry.CurrentUser.OpenSubKey(path);
            return new RegistryBackup(path, key is null ? null : Node.From(key));
        }

        public void Dispose()
        {
            Registry.CurrentUser.DeleteSubKeyTree(_path, throwOnMissingSubKey: false);
            if (_snapshot is null) return;
            using var key = Registry.CurrentUser.CreateSubKey(_path, writable: true);
            _snapshot.WriteTo(key);
        }

        private sealed record Node(
            List<(string Name, object Value, RegistryValueKind Kind)> Values,
            List<(string Name, Node Child)> Children)
        {
            public static Node From(RegistryKey key)
            {
                var values = key.GetValueNames()
                    .Select(n => (n, key.GetValue(n, null, RegistryValueOptions.DoNotExpandEnvironmentNames)!, key.GetValueKind(n)))
                    .ToList();
                var children = new List<(string, Node)>();
                foreach (var name in key.GetSubKeyNames())
                {
                    using var child = key.OpenSubKey(name)!;
                    children.Add((name, From(child)));
                }
                return new Node(values, children);
            }

            public void WriteTo(RegistryKey key)
            {
                foreach (var (name, value, kind) in Values)
                    key.SetValue(name, value, kind);
                foreach (var (name, child) in Children)
                {
                    using var sub = key.CreateSubKey(name, writable: true);
                    child.WriteTo(sub);
                }
            }
        }
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

    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
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
        [PreserveSig] int SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        [PreserveSig] int SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, uint reserved);
        [PreserveSig] int Resolve(IntPtr hwnd, uint flags);
        [PreserveSig] int SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }
}
