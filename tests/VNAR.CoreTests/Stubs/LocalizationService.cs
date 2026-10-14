namespace LocaleGameHub.Services;

/// <summary>Test stand-in for the WPF LocalizationService (only the members core code uses).</summary>
public static class LocalizationService
{
    public static bool IsSpanish => true;
    public static string Bi(string spanish, string english) => spanish;
}
