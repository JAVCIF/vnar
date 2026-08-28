using System.Diagnostics;
using System.Xml.Linq;
using LocaleGameHub.Models;

namespace LocaleGameHub.Services;

public sealed class LocaleEmulatorService
{
    private readonly LibraryService _library;

    public LocaleEmulatorService(LibraryService library)
    {
        _library = library;
    }

    public IReadOnlyList<LocaleProfile> ReadProfiles()
    {
        var leProc = _library.Settings.LocaleEmulatorPath;
        if (string.IsNullOrWhiteSpace(leProc) || !File.Exists(leProc))
            return [];

        var config = Path.Combine(Path.GetDirectoryName(leProc)!, "LEConfig.xml");
        if (!File.Exists(config))
            return [];

        try
        {
            var doc = XDocument.Load(config);
            return doc.Descendants("Profile").Select(p => new LocaleProfile
            {
                Name = (string?)p.Attribute("Name") ?? string.Empty,
                Guid = (string?)p.Attribute("Guid") ?? string.Empty,
                Location = (string?)p.Element("Location") ?? (string?)p.Element("Region") ?? string.Empty,
                RunAsAdmin = bool.TryParse((string?)p.Element("RunAsAdmin"), out var admin) && admin
            }).Where(p => !string.IsNullOrWhiteSpace(p.Guid)).ToList();
        }
        catch
        {
            return [];
        }
    }

    public void AutoAssignJapaneseProfiles()
    {
        var profiles = ReadProfiles();
        if (profiles.Count == 0)
            return;

        var normal = profiles.FirstOrDefault(p =>
            p.Guid == _library.Settings.NormalProfileGuid && !p.RunAsAdmin)
            ?? profiles.FirstOrDefault(p =>
                !p.RunAsAdmin && p.Location.Equals("ja-JP", StringComparison.OrdinalIgnoreCase))
            ?? profiles.FirstOrDefault(p => !p.RunAsAdmin && p.Name.Contains("Japanese", StringComparison.OrdinalIgnoreCase));

        var admin = profiles.FirstOrDefault(p =>
            p.Guid == _library.Settings.AdminProfileGuid && p.RunAsAdmin)
            ?? profiles.FirstOrDefault(p =>
                p.RunAsAdmin && p.Location.Equals("ja-JP", StringComparison.OrdinalIgnoreCase))
            ?? profiles.FirstOrDefault(p => p.RunAsAdmin && p.Name.Contains("Japanese", StringComparison.OrdinalIgnoreCase));

        if (normal is not null)
        {
            _library.Settings.NormalProfileGuid = normal.Guid;
            _library.Settings.NormalProfileName = normal.Name;
        }

        if (admin is not null)
        {
            _library.Settings.AdminProfileGuid = admin.Guid;
            _library.Settings.AdminProfileName = admin.Name;
        }

        _library.SaveSettings();
    }

    public void Launch(GameEntry game)
    {
        if (!File.Exists(game.ExePath))
            throw new FileNotFoundException(LocalizationService.Bi("No se encontró el ejecutable del juego.", "The game executable was not found."), game.ExePath);

        var leProc = _library.Settings.LocaleEmulatorPath;
        if (string.IsNullOrWhiteSpace(leProc) || !File.Exists(leProc))
            throw new InvalidOperationException(LocalizationService.Bi("Configura la ruta de LEProc.exe en Ajustes.", "Configure the LEProc.exe path in Settings."));

        var guid = game.RunAsAdmin
            ? _library.Settings.AdminProfileGuid
            : _library.Settings.NormalProfileGuid;

        if (string.IsNullOrWhiteSpace(guid))
        {
            AutoAssignJapaneseProfiles();
            guid = game.RunAsAdmin
                ? _library.Settings.AdminProfileGuid
                : _library.Settings.NormalProfileGuid;
        }

        if (string.IsNullOrWhiteSpace(guid))
            throw new InvalidOperationException(game.RunAsAdmin
                ? LocalizationService.Bi("No encontré un perfil japonés con administrador en LEConfig.xml. Revisa Ajustes.", "No administrator Japanese profile was found in LEConfig.xml. Check Settings.")
                : LocalizationService.Bi("No encontré un perfil japonés normal en LEConfig.xml. Revisa Ajustes.", "No normal Japanese profile was found in LEConfig.xml. Check Settings."));

        var quotedExe = Quote(game.ExePath);
        var args = $"-runas {guid} {quotedExe}";
        if (!string.IsNullOrWhiteSpace(game.Arguments))
            args += " " + game.Arguments.Trim();

        var psi = new ProcessStartInfo
        {
            FileName = leProc,
            Arguments = args,
            WorkingDirectory = Path.GetDirectoryName(game.ExePath) ?? Environment.CurrentDirectory,
            UseShellExecute = true
        };

        Process.Start(psi);
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
}
