using System.Text.Json.Serialization;
using LocaleGameHub.Services;

namespace LocaleGameHub.Models;

public sealed class AppSettings
{
    public string LocaleEmulatorPath { get; set; } = string.Empty;
    public string NormalProfileGuid { get; set; } = string.Empty;
    public string AdminProfileGuid { get; set; } = string.Empty;
    public string NormalProfileName { get; set; } = "Run in Japanese";
    public string AdminProfileName { get; set; } = "Run in Japanese (Admin)";

    /// <summary>SerpApi key encrypted with DPAPI (see SecretProtector). This is what is saved.</summary>
    public string SerpApiKeyProtected { get; set; } = string.Empty;

    /// <summary>
    /// Plain-text key written by Beta 1.2 and earlier. Only read, to migrate it
    /// (LibraryService.NormalizeSettings); never written again.
    /// </summary>
    [JsonPropertyName("SerpApiKey")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacySerpApiKey { get; set; }

    /// <summary>The usable key. Encrypted on assignment; not serialized.</summary>
    [JsonIgnore]
    public string SerpApiKey
    {
        get => SecretProtector.Unprotect(SerpApiKeyProtected);
        set => SerpApiKeyProtected = string.IsNullOrWhiteSpace(value) ? string.Empty : SecretProtector.Protect(value.Trim());
    }

    public string Language { get; set; } = string.Empty;
    public int PageSize { get; set; } = 20;

    /// <summary>Last shortcut type chosen in the shortcut window ("Modern" or "Classic").</summary>
    public string ShortcutKind { get; set; } = "Modern";
}
