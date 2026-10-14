using System.Text;

namespace LocaleGameHub.Services;

/// <summary>
/// Non-Windows stand-in for SecretProtector (DPAPI exists only on Windows). It is
/// reversible but never returns the plain text unchanged, so tests can still check
/// that settings.json does not contain the key in clear.
/// </summary>
public static class SecretProtector
{
    public static string Protect(string plainText)
        => string.IsNullOrEmpty(plainText) ? string.Empty : "TEST:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(new string(plainText.Reverse().ToArray())));

    public static string Unprotect(string protectedText)
    {
        if (string.IsNullOrWhiteSpace(protectedText) || !protectedText.StartsWith("TEST:", StringComparison.Ordinal)) return string.Empty;
        return new string(Encoding.UTF8.GetString(Convert.FromBase64String(protectedText[5..])).Reverse().ToArray());
    }
}
