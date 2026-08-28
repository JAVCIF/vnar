using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Xml.Linq;

namespace LocaleGameHub.Services;

public sealed class LocaleEmulatorInstallerService
{
    public const string RepositoryUrl = "https://github.com/xupefei/Locale-Emulator";
    private const string LatestReleaseApi = "https://api.github.com/repos/xupefei/Locale-Emulator/releases/latest";
    private const string FallbackDownloadUrl = "https://github.com/xupefei/Locale-Emulator/releases/download/v2.5.0.1/Locale.Emulator.2.5.0.1.zip";
    private const string FallbackTag = "v2.5.0.1";
    private const string FallbackAssetName = "Locale.Emulator.2.5.0.1.zip";

    private readonly HttpClient _http;

    public LocaleEmulatorInstallerService()
    {
        _http = new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("VNAR", "0.2.0"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<LocaleEmulatorInstallResult> DownloadLatestAsync(
        string destinationFolder,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(destinationFolder))
            throw new ArgumentException(LocalizationService.Bi("Selecciona una carpeta de destino.", "Select a destination folder."), nameof(destinationFolder));

        Directory.CreateDirectory(destinationFolder);
        progress?.Report(LocalizationService.Bi("Consultando la última versión publicada en GitHub…", "Checking the latest release published on GitHub…"));

        var tag = FallbackTag;
        var assetName = FallbackAssetName;
        var downloadUrl = FallbackDownloadUrl;

        try
        {
            using var releaseResponse = await _http.GetAsync(LatestReleaseApi, cancellationToken);
            releaseResponse.EnsureSuccessStatusCode();
            var releaseJson = await releaseResponse.Content.ReadAsStringAsync(cancellationToken);

            using var doc = JsonDocument.Parse(releaseJson);
            var root = doc.RootElement;
            tag = root.TryGetProperty("tag_name", out var tagNode) ? tagNode.GetString() ?? FallbackTag : FallbackTag;

            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var nameNode) ? nameNode.GetString() : null;
                    var url = asset.TryGetProperty("browser_download_url", out var urlNode) ? urlNode.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(name) && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(url))
                    {
                        assetName = name;
                        downloadUrl = url;
                        if (name.Contains("Locale.Emulator", StringComparison.OrdinalIgnoreCase))
                            break;
                    }
                }
            }
        }
        catch when (!cancellationToken.IsCancellationRequested)
        {
            progress?.Report(LocalizationService.Bi("No pude consultar la API de releases; usaré la release oficial v2.5.0.1 como respaldo…", "Could not query the releases API; using official release v2.5.0.1 as fallback…"));
        }

        var tempZip = Path.Combine(Path.GetTempPath(), $"LocaleGameHub_LE_{Guid.NewGuid():N}.zip");
        try
        {
            progress?.Report(LocalizationService.IsSpanish ? $"Descargando {assetName} ({tag})…" : $"Downloading {assetName} ({tag})…");
            using (var response = await _http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var target = File.Create(tempZip);
                await source.CopyToAsync(target, cancellationToken);
            }

            progress?.Report(LocalizationService.Bi("Descomprimiendo Locale Emulator…", "Extracting Locale Emulator…"));
            ZipFile.ExtractToDirectory(tempZip, destinationFolder, overwriteFiles: true);

            var leProc = Directory.EnumerateFiles(destinationFolder, "LEProc.exe", SearchOption.AllDirectories)
                .OrderBy(p => p.Count(c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar))
                .ThenBy(p => p.Length)
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(leProc) || !File.Exists(leProc))
                throw new InvalidOperationException(LocalizationService.Bi("La descarga terminó, pero no encontré LEProc.exe dentro del ZIP.", "The download completed, but LEProc.exe was not found inside the ZIP."));

            EnsureDefaultConfig(leProc);
            progress?.Report(LocalizationService.Bi("Locale Emulator quedó listo para usar.", "Locale Emulator is ready to use."));

            return new LocaleEmulatorInstallResult(leProc, tag, assetName);
        }
        finally
        {
            try
            {
                if (File.Exists(tempZip)) File.Delete(tempZip);
            }
            catch
            {
                // Temp cleanup failure is harmless.
            }
        }
    }

    public static void EnsureDefaultConfig(string leProcPath)
    {
        var folder = Path.GetDirectoryName(leProcPath);
        if (string.IsNullOrWhiteSpace(folder)) return;

        var configPath = Path.Combine(folder, "LEConfig.xml");
        if (File.Exists(configPath)) return;

        var profiles = new XElement("Profiles",
            BuildProfile("Run in Japanese", runAsAdmin: false),
            BuildProfile("Run in Japanese (Admin)", runAsAdmin: true));
        var tree = new XElement("LEConfig", profiles);
        tree.Save(configPath);
    }

    private static XElement BuildProfile(string name, bool runAsAdmin)
    {
        return new XElement("Profile",
            new XAttribute("Name", name),
            new XAttribute("Guid", Guid.NewGuid().ToString()),
            new XAttribute("MainMenu", false),
            new XElement("Parameter", string.Empty),
            new XElement("Location", "ja-JP"),
            new XElement("Timezone", "Tokyo Standard Time"),
            new XElement("RunAsAdmin", runAsAdmin),
            new XElement("RedirectRegistry", true),
            new XElement("IsAdvancedRedirection", false),
            new XElement("RunWithSuspend", false));
    }
}

public sealed record LocaleEmulatorInstallResult(string LeProcPath, string VersionTag, string AssetName);
