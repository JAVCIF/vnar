using System.Windows;
using System.Windows.Media.Imaging;
using LocaleGameHub.Models;
using LocaleGameHub.Services;
using Microsoft.Win32;

namespace LocaleGameHub;

public partial class DeveloperCoverWindow : Window
{
    private readonly LibraryService _library;
    private readonly DeveloperEntry _developer;
    private string _coverPath;
    private CoverEditState? _coverEditState;

    public DeveloperCoverWindow(LibraryService library, DeveloperEntry developer)
    {
        InitializeComponent();
        DarkTitleBarService.Apply(this);
        LocalizationService.Apply(this);
        _library = library;
        _developer = developer;
        _coverPath = developer.CoverPath;
        _coverEditState = developer.CoverEdit?.Clone();

        DeveloperNameText.Text = developer.Name;
        DeveloperIdText.Text = string.IsNullOrWhiteSpace(developer.VndbId) ? string.Empty : $"VNDB · {developer.VndbId}";
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        CoverPreview.Source = null;
        var hasCover = !string.IsNullOrWhiteSpace(_coverPath) && File.Exists(_coverPath);
        FallbackNameText.Visibility = hasCover ? Visibility.Collapsed : Visibility.Visible;
        FallbackNameText.Text = _developer.Name;
        EditButton.IsEnabled = hasCover;
        RemoveButton.IsEnabled = hasCover;

        if (!hasCover) return;
        try
        {
            _coverPath = ImageCompatibilityService.NormalizeFileIfNeeded(_coverPath, _developer.LocalId, "developer_compat");
            using var stream = new FileStream(_coverPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0) return;
            var bmp = new WriteableBitmap(decoder.Frames[0]);
            bmp.Freeze();
            CoverPreview.Source = bmp;
        }
        catch
        {
            CoverPreview.Source = null;
            FallbackNameText.Visibility = Visibility.Visible;
        }
    }

    private void ChooseImage_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = LocalizationService.Bi("Imágenes|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp|Todos los archivos|*.*", "Images|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp|All files|*.*"),
            Title = LocalizationService.Bi("Seleccionar portada del developer", "Select developer cover")
        };
        if (dialog.ShowDialog(this) == true) ImportImage(dialog.FileName, openEditor: true);
    }

    private void ImportImage(string path, bool openEditor)
    {
        try
        {
            _coverPath = CoverService.ImportCover(path, _developer.LocalId);
            _coverEditState = new CoverEditState { SourcePath = _coverPath };
            RefreshPreview();
            StatusText.Text = LocalizationService.Bi("✓ Imagen importada.", "✓ Image imported.");
            if (openEditor) OpenEditor();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, LocalizationService.Bi("Portada", "Cover"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void EditCover_Click(object sender, RoutedEventArgs e) => OpenEditor();

    private void OpenEditor()
    {
        var source = _coverEditState?.SourcePath;
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) source = _coverPath;
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) return;

        var editor = new CoverEditorWindow(_developer.LocalId, source, _coverEditState) { Owner = this };
        if (editor.ShowDialog() != true || string.IsNullOrWhiteSpace(editor.EditedCoverPath)) return;

        _coverPath = editor.EditedCoverPath;
        _coverEditState = editor.EditState?.Clone();
        RefreshPreview();
        StatusText.Text = LocalizationService.Bi("✓ Encuadre actualizado.", "✓ Framing updated.");
    }

    private void RemoveCover_Click(object sender, RoutedEventArgs e)
    {
        _coverPath = string.Empty;
        _coverEditState = null;
        RefreshPreview();
        StatusText.Text = LocalizationService.Bi("Portada eliminada. El nombre se mostrará centrado en la ficha.", "Cover removed. The developer name will be centered on the card.");
    }

    private void Cover_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DraggedImageService.TryGetImage(e.Data, out _, out _)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Cover_Drop(object sender, DragEventArgs e)
    {
        if (!DraggedImageService.TryGetImage(e.Data, out var localFile, out var remoteUrl)) return;

        if (!string.IsNullOrWhiteSpace(localFile))
        {
            ImportImage(localFile, openEditor: true);
            return;
        }

        if (string.IsNullOrWhiteSpace(remoteUrl)) return;

        StatusText.Text = LocalizationService.Bi(
            "Descargando imagen arrastrada desde el navegador…",
            "Downloading image dragged from the browser…");

        try
        {
            _coverPath = await RemoteImageService.DownloadUrlFromDropAsync(remoteUrl, _developer.LocalId);
            _coverEditState = new CoverEditState { SourcePath = _coverPath };
            RefreshPreview();
            StatusText.Text = LocalizationService.Bi(
                "✓ Imagen arrastrada desde el navegador e importada.",
                "✓ Image dragged from the browser and imported.");
            OpenEditor();
        }
        catch (Exception ex)
        {
            StatusText.Text = LocalizationService.Bi(
                "No pude importar la imagen arrastrada.",
                "Could not import the dragged image.");
            MessageBox.Show(
                this,
                LocalizationService.Bi(
                    "El navegador entregó una URL, pero no pude descargarla como imagen.\n\n",
                    "The browser provided a URL, but it could not be downloaded as an image.\n\n") + ex.Message,
                LocalizationService.Bi("Portada", "Cover"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _developer.CoverPath = _coverPath;
        _developer.CoverEdit = _coverEditState?.Clone();
        _library.SaveDevelopers();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
