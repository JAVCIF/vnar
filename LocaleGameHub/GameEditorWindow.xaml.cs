using System.Net;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media.Imaging;
using LocaleGameHub.Models;
using LocaleGameHub.Services;
using Microsoft.Win32;

namespace LocaleGameHub;

public partial class GameEditorWindow : Window
{
    private readonly LibraryService _library;
    private readonly GameEntry _game;
    private readonly VndbService _vndb = new();
    private string _coverPath;
    private CoverEditState? _coverEditState;
    private List<VndbDeveloperRef> _developers;
    private string _developerMetadataVndbId;

    public GameEditorWindow(LibraryService library, GameEntry game)
    {
        InitializeComponent();
        DarkTitleBarService.Apply(this);
        LocalizationService.Apply(this);
        _library = library;
        _game = game;
        _coverPath = game.CoverPath;
        _coverEditState = game.CoverEdit?.Clone();
        _developers = game.Developers.Select(d => new VndbDeveloperRef { Id = d.Id, Name = d.Name }).ToList();
        _developerMetadataVndbId = VndbService.NormalizeId(game.VndbId);

        NameBox.Text = game.Name;
        ExeBox.Text = game.ExePath;
        ArgumentsBox.Text = game.Arguments;
        VndbBox.Text = game.VndbId;
        AdminCheck.IsChecked = game.RunAsAdmin;
        FavoriteCheck.IsChecked = game.Favorite;
        SetCoverPreview(_coverPath);
    }

    private void SetCoverPreview(string? path)
    {
        CoverPreview.Source = null;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

        try
        {
            var displayPath = ImageCompatibilityService.NormalizeFileIfNeeded(path!, _game.Id, "preview_compat");
            if (!string.Equals(displayPath, path, StringComparison.OrdinalIgnoreCase))
            {
                _coverPath = displayPath;
                if (_coverEditState is not null && string.Equals(_coverEditState.SourcePath, path, StringComparison.OrdinalIgnoreCase))
                    _coverEditState.SourcePath = displayPath;
            }

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(displayPath, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            CoverPreview.Source = bmp;
        }
        catch
        {
            CoverPreview.Source = null;
        }
    }

    private void ChooseCover_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = LocalizationService.Bi("Imágenes|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp|Todos los archivos|*.*", "Images|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp|All files|*.*"),
            Title = LocalizationService.Bi("Seleccionar portada", "Select cover")
        };
        if (dialog.ShowDialog(this) == true) ImportLocalCover(dialog.FileName);
    }

    private void ImportLocalCover(string path)
    {
        try
        {
            _coverPath = CoverService.ImportCover(path, _game.Id);
            ResetCoverEditState(_coverPath);
            SetCoverPreview(_coverPath);
            CoverStatus.Text = LocalizationService.Bi("✓ Portada local importada.", "✓ Local cover imported.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, LocalizationService.Bi("Portada", "Cover"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void EditCover_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_coverPath) || !File.Exists(_coverPath))
        {
            MessageBox.Show(this,
                LocalizationService.Bi("Primero selecciona, descarga o arrastra una portada para poder editarla.", "First select, download, or drag a cover before editing it."),
                LocalizationService.Bi("Editar portada", "Edit cover"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var sourcePath = _coverEditState?.SourcePath;
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            sourcePath = CoverService.ResolveEditableSource(_game.Id, _coverPath);
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                sourcePath = _coverPath;

            _coverEditState = new CoverEditState { SourcePath = sourcePath };
        }

        var dialog = new CoverEditorWindow(_game.Id, sourcePath, _coverEditState) { Owner = this };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.EditedCoverPath)) return;

        _coverPath = dialog.EditedCoverPath;
        _coverEditState = dialog.EditState?.Clone();
        SetCoverPreview(_coverPath);
        CoverStatus.Text = dialog.OutputDescription is { Length: > 0 }
            ? (LocalizationService.IsSpanish ? $"✓ Portada editada · {dialog.OutputDescription}." : $"✓ Cover edited · {dialog.OutputDescription}.")
            : LocalizationService.Bi("✓ Portada editada: zoom, posición y fondo aplicados.", "✓ Cover edited: zoom, position, and background applied.");
    }

    private void ResetCoverEditState(string sourcePath)
    {
        _coverEditState = new CoverEditState
        {
            SourcePath = sourcePath,
            Zoom = 1.0,
            FocusX = 0.5,
            FocusY = 0.5,
            BackgroundMode = "black",
            ImproveQuality = false
        };
    }

    private async void CoverSearch_Click(object sender, RoutedEventArgs e)
    {
        var query = BuildDefaultCoverQuery();
        var dialog = new CoverSearchWindow(_library, query) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.SelectedResult is not { } result) return;

        CoverStatus.Text = LocalizationService.IsSpanish ? $"Descargando portada desde {result.Source}…" : $"Downloading cover from {result.Source}…";
        try
        {
            _coverPath = await RemoteImageService.DownloadCoverAsync(
                result.ImageUrl,
                result.ThumbnailUrl,
                _game.Id,
                result.VndbId is null ? "google" : "vndbsearch");
            ResetCoverEditState(_coverPath);
            SetCoverPreview(_coverPath);

            if (!string.IsNullOrWhiteSpace(result.VndbId))
            {
                VndbBox.Text = result.VndbId;
                if (!string.IsNullOrWhiteSpace(result.Title)) NameBox.Text = result.Title;
                try
                {
                    var metadata = await _vndb.GetVisualNovelAsync(result.VndbId);
                    if (metadata is not null)
                    {
                        _developers = metadata.Developers.Select(d => new VndbDeveloperRef { Id = d.Id, Name = d.Name }).ToList();
                        _developerMetadataVndbId = metadata.Id;
                    }
                }
                catch
                {
                    _developers = [];
                    _developerMetadataVndbId = VndbService.NormalizeId(result.VndbId);
                }
            }

            CoverStatus.Text = LocalizationService.IsSpanish ? $"✓ Portada seleccionada: {result.Source}." : $"✓ Cover selected: {result.Source}.";
        }
        catch (Exception ex)
        {
            CoverStatus.Text = LocalizationService.Bi("No pude descargar esa imagen.", "Could not download that image.");
            MessageBox.Show(this, ex.Message, LocalizationService.Bi("Portada", "Cover"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private string BuildDefaultCoverQuery()
    {
        var exe = ExeBox.Text.Trim();
        try
        {
            var folder = Path.GetDirectoryName(exe);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                var folderName = new DirectoryInfo(folder).Name.Trim();
                if (!string.IsNullOrWhiteSpace(folderName)) return folderName;
            }
        }
        catch { }

        var name = NameBox.Text.Trim();
        return string.IsNullOrWhiteSpace(name) ? "visual novel" : name;
    }

    private async void Vndb_Click(object sender, RoutedEventArgs e)
    {
        VndbButton.IsEnabled = false;
        VndbStatus.Text = LocalizationService.Bi("Consultando VNDB…", "Querying VNDB…");
        try
        {
            var result = await _vndb.GetVisualNovelAsync(VndbBox.Text);
            if (result is null)
            {
                VndbStatus.Text = LocalizationService.Bi("No encontré ese ID en VNDB.", "That ID was not found on VNDB.");
                return;
            }

            VndbBox.Text = result.Id;
            NameBox.Text = result.Title;
            _developers = result.Developers.Select(d => new VndbDeveloperRef { Id = d.Id, Name = d.Name }).ToList();
            _developerMetadataVndbId = result.Id;
            if (string.IsNullOrWhiteSpace(result.ImageUrl))
            {
                VndbStatus.Text = LocalizationService.IsSpanish ? $"Encontré {result.Title}, pero la entrada no tiene portada." : $"Found {result.Title}, but the entry has no cover.";
                return;
            }

            _coverPath = await _vndb.DownloadCoverAsync(result.ImageUrl, _game.Id);
            ResetCoverEditState(_coverPath);
            SetCoverPreview(_coverPath);
            VndbStatus.Text = LocalizationService.IsSpanish ? $"✓ {result.Title}: portada descargada." : $"✓ {result.Title}: cover downloaded.";
            CoverStatus.Text = LocalizationService.Bi("✓ Portada obtenida por ID de VNDB.", "✓ Cover fetched by VNDB ID.");
        }
        catch (Exception ex)
        {
            VndbStatus.Text = LocalizationService.Bi("Error al consultar VNDB: ", "Error querying VNDB: ") + ex.Message;
        }
        finally
        {
            VndbButton.IsEnabled = true;
        }
    }

    private void ChangeExe_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = LocalizationService.Bi("Ejecutables (*.exe)|*.exe", "Executables (*.exe)|*.exe"),
            Title = LocalizationService.Bi("Seleccionar ejecutable", "Select executable")
        };
        if (File.Exists(ExeBox.Text)) dialog.InitialDirectory = Path.GetDirectoryName(ExeBox.Text);
        if (dialog.ShowDialog(this) == true) ExeBox.Text = dialog.FileName;
    }

    private bool TryApplyCurrentGameFields()
    {
        var exe = ExeBox.Text.Trim();
        if (!File.Exists(exe))
        {
            MessageBox.Show(this, LocalizationService.Bi("El ejecutable no existe.", "The executable does not exist."), LocalizationService.Bi("Juego", "Game"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            MessageBox.Show(this, LocalizationService.Bi("Escribe un nombre para el juego.", "Enter a name for the game."), LocalizationService.Bi("Juego", "Game"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        _game.Name = NameBox.Text.Trim();
        _game.ExePath = exe;
        _game.Arguments = ArgumentsBox.Text.Trim();
        _game.CoverPath = _coverPath;
        _game.CoverEdit = _coverEditState?.Clone();
        var normalizedVndbId = VndbService.NormalizeId(VndbBox.Text);
        _game.VndbId = normalizedVndbId;
        _game.Developers = string.Equals(normalizedVndbId, _developerMetadataVndbId, StringComparison.OrdinalIgnoreCase)
            ? _developers.Select(d => new VndbDeveloperRef { Id = d.Id, Name = d.Name }).ToList()
            : [];
        _game.RunAsAdmin = AdminCheck.IsChecked == true;
        _game.Favorite = FavoriteCheck.IsChecked == true;
        _library.SaveGames();
        return true;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!TryApplyCurrentGameFields()) return;
        DialogResult = true;
    }

    private void CreateShortcut_Click(object sender, RoutedEventArgs e)
    {
        if (!TryApplyCurrentGameFields()) return;
        var dialog = new ShortcutWindow(_game) { Owner = this };
        dialog.ShowDialog();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(this,
            LocalizationService.Bi("Esto solo quita la entrada del Hub; no borra ningún archivo del juego. ¿Continuar?", "This only removes the entry from VNAR; it does not delete any game files. Continue?"),
            LocalizationService.Bi("Eliminar de la biblioteca", "Remove from library"), MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;
        _library.Remove(_game);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Cover_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = TryGetDroppedImage(e.Data, out _, out _) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Cover_Drop(object sender, DragEventArgs e)
    {
        if (!TryGetDroppedImage(e.Data, out var localFile, out var remoteUrl)) return;

        if (!string.IsNullOrWhiteSpace(localFile))
        {
            ImportLocalCover(localFile);
            return;
        }

        if (string.IsNullOrWhiteSpace(remoteUrl)) return;
        CoverStatus.Text = LocalizationService.Bi("Descargando imagen arrastrada desde el navegador…", "Downloading image dragged from the browser…");
        try
        {
            _coverPath = await RemoteImageService.DownloadUrlFromDropAsync(remoteUrl, _game.Id);
            ResetCoverEditState(_coverPath);
            SetCoverPreview(_coverPath);
            CoverStatus.Text = LocalizationService.Bi("✓ Imagen arrastrada desde el navegador e importada.", "✓ Image dragged from the browser and imported.");
        }
        catch (Exception ex)
        {
            CoverStatus.Text = LocalizationService.Bi("No pude importar la imagen arrastrada.", "Could not import the dragged image.");
            MessageBox.Show(this,
                LocalizationService.Bi("El navegador entregó una URL, pero no pude descargarla como imagen.\n\n", "The browser provided a URL, but it could not be downloaded as an image.\n\n") + ex.Message,
                LocalizationService.Bi("Portada", "Cover"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private static bool TryGetDroppedImage(IDataObject data, out string? localFile, out string? remoteUrl)
    {
        localFile = null;
        remoteUrl = null;

        if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files)
        {
            var file = files.FirstOrDefault(f => File.Exists(f) && CoverService.IsImage(f));
            if (!string.IsNullOrWhiteSpace(file))
            {
                localFile = file;
                return true;
            }
        }

        // Chrome/Edge normally include an HTML fragment when an actual image is dragged.
        // Prefer its <img src> over a generic URL, which may point to the Google results page.
        try
        {
            if (data.GetDataPresent(DataFormats.Html) && data.GetData(DataFormats.Html) is string html)
            {
                var match = Regex.Match(html, "<img[^>]+(?:src|data-src|data-iurl|data-original)\\s*=\\s*[\\\"'](?<url>https?://[^\\\"']+)", RegexOptions.IgnoreCase);
                if (match.Success && TryNormalizeHttpUrl(WebUtility.HtmlDecode(match.Groups["url"].Value), out var url))
                {
                    remoteUrl = url;
                    return true;
                }

                // Google can also embed the original image URL as imgurl=... inside a result link.
                match = Regex.Match(html, "(?:imgurl|mediaurl)=(?<url>https?%3A%2F%2F[^&\"'<>]+|https?://[^&\"'<>]+)", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var encoded = WebUtility.HtmlDecode(match.Groups["url"].Value);
                    var decoded = encoded.Contains('%') ? Uri.UnescapeDataString(encoded) : encoded;
                    if (TryNormalizeHttpUrl(decoded, out url))
                    {
                        remoteUrl = url;
                        return true;
                    }
                }
            }
        }
        catch { }

        foreach (var format in new[] { "UniformResourceLocatorW", "UniformResourceLocator", DataFormats.UnicodeText, DataFormats.Text })
        {
            try
            {
                if (!data.GetDataPresent(format)) continue;
                var value = data.GetData(format)?.ToString()?.Trim().Trim('\0');
                if (TryNormalizeHttpUrl(value, out var url))
                {
                    remoteUrl = url;
                    return true;
                }
            }
            catch { }
        }

        return false;
    }

    private static bool TryNormalizeHttpUrl(string? value, out string url)
    {
        url = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;
        value = WebUtility.HtmlDecode(value.Trim());
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;

        // A dragged Google Images result can be an /imgres URL. Extract the real image URL when present.
        if (uri.Host.Contains("google.", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var pair = part.Split('=', 2);
                if (pair.Length != 2) continue;
                var key = Uri.UnescapeDataString(pair[0]);
                if (!key.Equals("imgurl", StringComparison.OrdinalIgnoreCase) &&
                    !key.Equals("mediaurl", StringComparison.OrdinalIgnoreCase)) continue;

                var candidate = Uri.UnescapeDataString(pair[1]);
                if (Uri.TryCreate(candidate, UriKind.Absolute, out var imageUri) && imageUri.Scheme is "http" or "https")
                {
                    url = imageUri.AbsoluteUri;
                    return true;
                }
            }
        }

        url = uri.AbsoluteUri;
        return true;
    }
}
