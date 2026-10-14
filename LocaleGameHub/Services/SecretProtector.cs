using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace LocaleGameHub.Services;

/// <summary>
/// Encrypts small secrets (the SerpApi key) with Windows DPAPI for the current user.
/// Only that Windows account on that PC can decrypt them, so copying settings.json
/// elsewhere (or sharing it in a bug report) does not leak the key.
/// Calls crypt32.dll directly (the same API ProtectedData wraps) so VNAR needs no
/// extra package for it.
/// </summary>
public static class SecretProtector
{
    // Ties the ciphertext to VNAR: other apps using DPAPI for the same user cannot
    // decrypt it by accident without this value.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("JAVCIF.VNAR.SerpApiKey.v1");

    private const int CryptProtectUiForbidden = 0x1;

    public static string Protect(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return string.Empty;
        return Convert.ToBase64String(Transform(Encoding.UTF8.GetBytes(plainText), protect: true));
    }

    /// <summary>
    /// Returns the secret, or an empty string if it cannot be decrypted (for example
    /// settings copied from another user or PC); the user simply enters it again.
    /// </summary>
    public static string Unprotect(string protectedBase64)
    {
        if (string.IsNullOrWhiteSpace(protectedBase64)) return string.Empty;
        try
        {
            return Encoding.UTF8.GetString(Transform(Convert.FromBase64String(protectedBase64), protect: false));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return string.Empty;
        }
    }

    private static byte[] Transform(byte[] input, bool protect)
    {
        var inputHandle = GCHandle.Alloc(input, GCHandleType.Pinned);
        var entropyHandle = GCHandle.Alloc(Entropy, GCHandleType.Pinned);
        var output = default(DataBlob);
        try
        {
            var inBlob = new DataBlob { Size = input.Length, Data = inputHandle.AddrOfPinnedObject() };
            var entropyBlob = new DataBlob { Size = Entropy.Length, Data = entropyHandle.AddrOfPinnedObject() };

            var ok = protect
                ? CryptProtectData(ref inBlob, "VNAR", ref entropyBlob, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out output)
                : CryptUnprotectData(ref inBlob, IntPtr.Zero, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out output);
            if (!ok)
                throw new CryptographicException(Marshal.GetLastWin32Error());

            var result = new byte[output.Size];
            if (output.Size > 0) Marshal.Copy(output.Data, result, 0, output.Size);
            return result;
        }
        finally
        {
            if (output.Data != IntPtr.Zero)
            {
                // Clear the plaintext copy returned by Unprotect before freeing it.
                if (!protect && output.Size > 0)
                    Marshal.Copy(new byte[output.Size], 0, output.Data, output.Size);
                LocalFree(output.Data);
            }
            entropyHandle.Free();
            inputHandle.Free();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn, string? description, ref DataBlob optionalEntropy,
        IntPtr reserved, IntPtr promptStruct, int flags, out DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn, IntPtr description, ref DataBlob optionalEntropy,
        IntPtr reserved, IntPtr promptStruct, int flags, out DataBlob dataOut);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr memory);
}
