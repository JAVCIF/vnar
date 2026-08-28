namespace LocaleGameHub.Models;

public sealed class AppSettings
{
    public string LocaleEmulatorPath { get; set; } = string.Empty;
    public string NormalProfileGuid { get; set; } = string.Empty;
    public string AdminProfileGuid { get; set; } = string.Empty;
    public string NormalProfileName { get; set; } = "Run in Japanese";
    public string AdminProfileName { get; set; } = "Run in Japanese (Admin)";
    public string SerpApiKey { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public int PageSize { get; set; } = 20;
}
