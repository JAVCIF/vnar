using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using LocaleGameHub.Models;

namespace LocaleGameHub.Services;

/// <summary>
/// Gives the game's windows the identity of the classic shortcut that launched it.
///
/// A classic .lnk carries the game's AppUserModelID, but Windows only passes it on
/// as long as every process in the chain cooperates. Some games start through a
/// tiny loader that creates the real game (e.g. "main.bin") with an empty
/// STARTUPINFO and exits; the game window then has no link to the shortcut, so
/// Windows shows a second taskbar button next to the pinned one and cannot pin the
/// running game (it does not know how to relaunch a ".bin").
///
/// After a classic launch, VNAR watches for visible top-level windows of processes
/// whose executable lives in the game's folder and sets on each one the game's
/// AppUserModelID plus a relaunch command ("VNAR.exe --launch &lt;id&gt;"), name and
/// icon. The windows then group with the pinned shortcut, and pinning the running
/// game pins a VNAR shortcut. Windows of elevated games cannot be changed from a
/// non-elevated VNAR; those are skipped.
/// </summary>
public sealed class GameWindowIdentityService
{
    // PKEY_AppUserModel_* share this format ID.
    private static readonly Guid AppUserModelFormat = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
    public const uint AppIdPropertyId = 5;
    public const uint RelaunchCommandPropertyId = 2;
    public const uint RelaunchIconPropertyId = 3;
    public const uint RelaunchNamePropertyId = 4;
    public const uint PreventPinningPropertyId = 9;
    private static Guid PropertyStoreIid = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");

    /// <summary>How long to wait for the first game window (loaders, config dialogs, slow starts).</summary>
    public static readonly TimeSpan FirstWindowTimeout = TimeSpan.FromMinutes(3);

    /// <summary>Keep watching this long after the last game process ended (a launcher handing over to the game).</summary>
    private static readonly TimeSpan ExitGrace = TimeSpan.FromSeconds(15);

    private readonly string _gameFolder;
    private readonly string _vnarExe;
    private readonly int _ownProcessId = Environment.ProcessId;
    private readonly Dictionary<int, bool> _isGameProcess = new();
    private readonly HashSet<int> _gameProcesses = new();
    private readonly HashSet<IntPtr> _handled = new();

    /// <param name="preventPinning">
    /// Launches from the library window: the game still groups with a pinned classic
    /// shortcut, but cannot be pinned from its own taskbar button. Pinning stays a
    /// deliberate choice made by creating a classic shortcut, and nothing pinned this
    /// way can point at the game's executable without Locale Emulator.
    /// </param>
    public GameWindowIdentityService(GameEntry game, string vnarExe, string relaunchIcon, bool preventPinning = false)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(game.ExePath))
            ?? throw new InvalidOperationException("The game executable has no folder.");
        _gameFolder = Path.EndsInDirectorySeparator(folder) ? folder : folder + Path.DirectorySeparatorChar;
        _vnarExe = Path.GetFullPath(vnarExe);

        AppId = TaskbarIdentityService.GetGameAppId(game.Id);
        RelaunchCommand = $"\"{_vnarExe}\" --launch {game.Id:D}";
        RelaunchIcon = relaunchIcon;
        DisplayName = game.Name;
        PreventPinning = preventPinning;
    }

    public string AppId { get; }
    public string RelaunchCommand { get; }
    public string RelaunchIcon { get; }
    public string DisplayName { get; }
    public bool PreventPinning { get; }

    /// <summary>
    /// Watches until the game has closed (or no game window appeared in time).
    /// Run it on an STA thread.
    /// </summary>
    public void Watch()
    {
        var clock = Stopwatch.StartNew();
        TimeSpan? lastAlive = null;
        while (true)
        {
            TagNewWindows();
            if (AnyGameProcessAlive()) lastAlive = clock.Elapsed;

            if (lastAlive is null)
            {
                if (clock.Elapsed > FirstWindowTimeout) return;
            }
            else if (clock.Elapsed - lastAlive.Value > ExitGrace)
            {
                return;
            }

            // Poll quickly until the game shows up, then just catch windows it
            // creates later (e.g. when switching to full screen).
            Thread.Sleep(lastAlive is null ? 250 : 1000);
        }
    }

    /// <summary>Sets the identity on game windows not handled yet. Returns how many were changed.</summary>
    public int TagNewWindows()
    {
        var windows = new List<(IntPtr Handle, int ProcessId)>();
        EnumWindows((hwnd, _) =>
        {
            if (IsWindowVisible(hwnd))
            {
                GetWindowThreadProcessId(hwnd, out var pid);
                windows.Add((hwnd, (int)pid));
            }
            return true;
        }, IntPtr.Zero);

        // Forget closed windows and processes without windows, so a reused handle
        // or process ID is looked at again.
        _handled.RemoveWhere(h => !IsWindow(h));
        var seen = windows.Select(w => w.ProcessId).ToHashSet();
        foreach (var pid in _isGameProcess.Keys.Where(p => !seen.Contains(p)).ToList())
            _isGameProcess.Remove(pid);

        var changed = 0;
        foreach (var (hwnd, pid) in windows)
        {
            if (!IsGameProcess(pid)) continue;
            _gameProcesses.Add(pid);
            if (!_handled.Add(hwnd)) continue;

            // A failure (usually an elevated game) is not retried for that window.
            if (TryApply(hwnd)) changed++;
        }
        return changed;
    }

    private bool IsGameProcess(int pid)
    {
        if (pid == _ownProcessId || pid == 0) return false;
        if (_isGameProcess.TryGetValue(pid, out var known)) return known;

        var path = TryGetProcessPath(pid);
        var isGame = path is not null &&
                     path.StartsWith(_gameFolder, StringComparison.OrdinalIgnoreCase) &&
                     !string.Equals(path, _vnarExe, StringComparison.OrdinalIgnoreCase);
        _isGameProcess[pid] = isGame;
        return isGame;
    }

    private bool AnyGameProcessAlive()
    {
        _gameProcesses.RemoveWhere(pid => !IsProcessRunning(pid));
        return _gameProcesses.Count > 0;
    }

    private bool TryApply(IntPtr hwnd)
    {
        try
        {
            if (PreventPinning) ApplyUnpinnable(hwnd, AppId);
            else ApplyToWindow(hwnd, AppId, RelaunchCommand, RelaunchIcon, DisplayName);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Sets the relaunch properties first and the AppUserModelID last: changing the
    /// ID is what makes the taskbar regroup the window and read the rest.
    /// </summary>
    public static void ApplyToWindow(IntPtr hwnd, string appId, string relaunchCommand, string relaunchIcon, string displayName)
    {
        Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(hwnd, ref PropertyStoreIid, out var store));
        try
        {
            SetString(store, RelaunchCommandPropertyId, relaunchCommand);
            SetString(store, RelaunchIconPropertyId, relaunchIcon);
            SetString(store, RelaunchNamePropertyId, displayName);
            SetString(store, AppIdPropertyId, appId);
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }

    /// <summary>The game's AppUserModelID, marked as not pinnable (PreventPinning must be set before the ID).</summary>
    public static void ApplyUnpinnable(IntPtr hwnd, string appId)
    {
        Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(hwnd, ref PropertyStoreIid, out var store));
        try
        {
            var key = new TaskbarIdentityService.PropertyKey { FormatId = AppUserModelFormat, PropertyId = PreventPinningPropertyId };
            var value = new TaskbarIdentityService.PropVariant { ValueType = 11, BoolValue = -1 }; // VT_BOOL, VARIANT_TRUE
            Marshal.ThrowExceptionForHR(store.SetValue(ref key, ref value));
            SetString(store, AppIdPropertyId, appId);
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }

    /// <summary>Reads PKEY_AppUserModel_PreventPinning (used by the shell tests).</summary>
    public static bool ReadWindowPreventPinning(IntPtr hwnd)
    {
        Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(hwnd, ref PropertyStoreIid, out var store));
        try
        {
            var key = new TaskbarIdentityService.PropertyKey { FormatId = AppUserModelFormat, PropertyId = PreventPinningPropertyId };
            Marshal.ThrowExceptionForHR(store.GetValue(ref key, out var value));
            try { return value.ValueType == 11 && value.BoolValue != 0; }
            finally { PropVariantClear(ref value); }
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }

    /// <summary>Reads one of the window's AppUserModel strings (used by the shell tests).</summary>
    public static string? ReadWindowString(IntPtr hwnd, uint propertyId)
    {
        Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(hwnd, ref PropertyStoreIid, out var store));
        try
        {
            var key = new TaskbarIdentityService.PropertyKey { FormatId = AppUserModelFormat, PropertyId = propertyId };
            Marshal.ThrowExceptionForHR(store.GetValue(ref key, out var value));
            try
            {
                return value.ValueType == 31 ? Marshal.PtrToStringUni(value.StringValue) : null;
            }
            finally
            {
                PropVariantClear(ref value);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }

    private static void SetString(TaskbarIdentityService.IPropertyStore store, uint propertyId, string text)
    {
        var key = new TaskbarIdentityService.PropertyKey { FormatId = AppUserModelFormat, PropertyId = propertyId };
        var value = new TaskbarIdentityService.PropVariant
        {
            ValueType = 31, // VT_LPWSTR
            StringValue = Marshal.StringToCoTaskMemUni(text)
        };
        try
        {
            Marshal.ThrowExceptionForHR(store.SetValue(ref key, ref value));
        }
        finally
        {
            Marshal.FreeCoTaskMem(value.StringValue);
        }
    }

    // ------------------------------------------------------------- relaunch icon

    /// <summary>
    /// The icon to show for the running game and for a shortcut pinned from it:
    /// the icon of the .lnk that started VNAR (Windows passes its path in
    /// STARTUPINFO), else the game's executable, else VNAR's generic icon.
    /// </summary>
    public static string ResolveRelaunchIcon(GameEntry game)
    {
        var fromLink = TryReadLaunchingLinkIcon();
        if (fromLink is not null) return fromLink;

        if (File.Exists(game.ExePath) && ExtractIconEx(game.ExePath, -1, null, null, 0) > 0)
            return game.ExePath + ",0";

        return ShortcutService.GetGenericIconPath() + ",0";
    }

    private static string? TryReadLaunchingLinkIcon()
    {
        try
        {
            var info = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>() };
            GetStartupInfoW(ref info);
            if ((info.Flags & StartfTitleIsLinkName) == 0 || info.Title == IntPtr.Zero) return null;

            var linkPath = Marshal.PtrToStringUni(info.Title);
            if (string.IsNullOrWhiteSpace(linkPath) || !File.Exists(linkPath)) return null;

            var link = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"), true)!)!;
            try
            {
                ((System.Runtime.InteropServices.ComTypes.IPersistFile)link).Load(linkPath, 0);
                var buffer = new StringBuilder(1024);
                if (((IShellLinkWIcon)link).GetIconLocation(buffer, buffer.Capacity, out var index) != 0) return null;
                var iconPath = Environment.ExpandEnvironmentVariables(buffer.ToString());
                return File.Exists(iconPath) ? $"{iconPath},{index}" : null;
            }
            finally
            {
                Marshal.FinalReleaseComObject(link);
            }
        }
        catch
        {
            return null;
        }
    }

    // ------------------------------------------------------------------ interop

    private const int StartfTitleIsLinkName = 0x800;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint StillActive = 259;

    private static string? TryGetProcessPath(int pid)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, (uint)pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var buffer = new StringBuilder(32768);
            var size = buffer.Capacity;
            return QueryFullProcessImageNameW(handle, 0, buffer, ref size) ? buffer.ToString(0, size) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static bool IsProcessRunning(int pid)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, (uint)pid);
        if (handle == IntPtr.Zero) return false;
        try
        {
            return GetExitCodeProcess(handle, out var code) && code == StillActive;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, StringBuilder exeName, ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern void GetStartupInfoW(ref StartupInfo info);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, IntPtr[]? large, IntPtr[]? small, uint icons);

    [DllImport("shell32.dll")]
    private static extern int SHGetPropertyStoreForWindow(
        IntPtr hwnd, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out TaskbarIdentityService.IPropertyStore store);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref TaskbarIdentityService.PropVariant value);

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        public int Size;
        public IntPtr Reserved;
        public IntPtr Desktop;
        public IntPtr Title;
        public int X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute;
        public int Flags;
        public short ShowWindow;
        public short Reserved2;
        public IntPtr Reserved2Pointer;
        public IntPtr StdInput, StdOutput, StdError;
    }

    // Vtable order of IShellLinkW up to GetIconLocation; only that getter is used.
    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkWIcon
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
        [PreserveSig] int GetIconLocation([MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int maxIconPath, out int iconIndex);
    }
}
