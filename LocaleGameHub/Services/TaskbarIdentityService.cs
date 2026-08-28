using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace LocaleGameHub.Services;

/// <summary>
/// Prevents Explorer from inferring one shared taskbar identity from VNAR.exe
/// for the library UI and every "--launch" shortcut.
/// </summary>
public static class TaskbarIdentityService
{
    // Keep this stable across versions, and in sync with installer/VNAR.iss.
    public const string LauncherAppId = "JAVCIF.VNAR.Launcher";

    public static string GetGameAppId(Guid gameId) => $"JAVCIF.VNAR.Game.{gameId:N}";

    public static void SetCurrentProcessIdentity(Guid? gameId = null)
    {
        var appId = gameId.HasValue ? GetGameAppId(gameId.Value) : LauncherAppId;
        Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID(appId));
    }

    public static void SetShortcutIdentity(string shortcutPath, Guid gameId)
    {
        // WScript.Shell exposes the icon/arguments but not System.AppUserModel.ID.
        // Reopen the saved link and change only that property, preserving its icon,
        // target, arguments and any existing Shell Link flags.
        var linkType = Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"), true)!;
        var link = Activator.CreateInstance(linkType)
            ?? throw new InvalidOperationException("Could not open the Shell Link property store.");

        try
        {
            var file = (IPersistFile)link;
            file.Load(shortcutPath, 2); // STGM_READWRITE
            var store = (IPropertyStore)link;
            var key = new PropertyKey
            {
                FormatId = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
                PropertyId = 5 // PKEY_AppUserModel_ID
            };
            var value = new PropVariant
            {
                ValueType = 31, // VT_LPWSTR
                StringValue = Marshal.StringToCoTaskMemUni(GetGameAppId(gameId))
            };

            try
            {
                Marshal.ThrowExceptionForHR(store.SetValue(ref key, ref value));
                Marshal.ThrowExceptionForHR(store.Commit());
                file.Save(shortcutPath, true);
            }
            finally
            {
                Marshal.FreeCoTaskMem(value.StringValue);
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey
    {
        public Guid FormatId;
        public uint PropertyId;
    }

    // PROPVARIANT's union contains a counted pointer: 16 bytes on x86,
    // 24 on x64. Include that member so the native layout is correct on both.
    [StructLayout(LayoutKind.Sequential)]
    private struct CountedPointer
    {
        public uint Count;
        public IntPtr Pointer;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort ValueType;
        [FieldOffset(8)] public IntPtr StringValue;
        [FieldOffset(8)] public CountedPointer ArrayValue;
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }
}
