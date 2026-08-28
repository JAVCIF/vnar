using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LocaleGameHub.Models;
using LocaleGameHub.Services;

namespace LocaleGameHub;

public partial class CoverSearchWindow : Window
{
    private readonly LibraryService _library;
    private readonly VndbService _vndb = new();
    private readonly ImageSearchService _images = new();
    private bool _busy;

    public CoverSearchResult? SelectedResult { get; private set; }

    public CoverSearchWindow(LibraryService library, string initialQuery)
    {
        InitializeComponent();
        DarkTitleBarService.Apply(this);
        LocalizationService.Apply(this);
        _library = library;
        QueryBox.Text = initialQuery;
        Loaded += async (_, _) => await SearchAsync();
    }

    private async void Search_Click(object sender, RoutedEventArgs e) => await SearchAsync();

    private async void QueryBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await SearchAsync();
    }

    private async Task SearchAsync()
    {
        if (_busy) return;
        var query = QueryBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            StatusText.Text = LocalizationService.Bi("Escribe un nombre para buscar.", "Enter a name to search.");
            return;
        }

        SetBusy(true);
        ResultsList.ItemsSource = null;
        try
        {
            var provider = SelectedProvider();
            IReadOnlyList<CoverSearchResult> results;
            if (provider == "google")
            {
                if (string.IsNullOrWhiteSpace(_library.Settings.SerpApiKey))
                {
                    StatusText.Text = LocalizationService.Bi("Google Images interno necesita una API key de SerpApi. Puedes añadirla en Ajustes; mientras tanto el drag directo desde el navegador ya funciona.", "Built-in Google Images needs a SerpApi API key. You can add it in Settings; meanwhile direct browser drag-and-drop still works.");
                    return;
                }

                StatusText.Text = LocalizationService.Bi("Buscando en Google Images…", "Searching Google Images…");
                results = await _images.SearchGoogleImagesAsync(query + " visual novel cover", _library.Settings.SerpApiKey, 10);
            }
            else
            {
                StatusText.Text = LocalizationService.Bi("Buscando coincidencias en VNDB…", "Searching VNDB matches…");
                results = await _vndb.SearchVisualNovelsAsync(query, 10);
            }

            ResultsList.ItemsSource = results;
            StatusText.Text = results.Count == 0
                ? LocalizationService.Bi("No encontré resultados. Prueba con otro nombre o cambia de proveedor.", "No results found. Try another name or change provider.")
                : (LocalizationService.IsSpanish ? $"✓ {results.Count} resultado(s). Haz clic en la portada que quieras usar." : $"✓ {results.Count} result(s). Click the cover you want to use.");
        }
        catch (Exception ex)
        {
            StatusText.Text = LocalizationService.Bi("No se pudo completar la búsqueda: ", "Search could not be completed: ") + ex.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void Result_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: CoverSearchResult result }) return;
        SelectedResult = result;
        DialogResult = true;
    }

    private void ProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        StatusText.Text = SelectedProvider() == "google"
            ? string.IsNullOrWhiteSpace(_library.Settings.SerpApiKey)
                ? LocalizationService.Bi("Google Images interno requiere SerpApi. Añade tu API key en Ajustes o usa VNDB, que no necesita clave.", "Built-in Google Images requires SerpApi. Add your API key in Settings or use VNDB, which needs no key.")
                : LocalizationService.Bi("Google Images usará SerpApi y mostrará las primeras 10 imágenes dentro del Hub.", "Google Images will use SerpApi and show the first 10 images inside VNAR.")
            : LocalizationService.Bi("VNDB no necesita API key y suele ser la mejor fuente para novelas visuales.", "VNDB does not require an API key and is usually the best source for visual novels.");
    }

    private void BrowserFallback_Click(object sender, RoutedEventArgs e)
    {
        var query = QueryBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(query)) query = "visual novel cover";
        CoverService.OpenGoogleImages(query + " visual novel cover");
    }

    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private string SelectedProvider()
        => ProviderCombo.SelectedItem is ComboBoxItem item ? item.Tag?.ToString() ?? "vndb" : "vndb";

    private void SetBusy(bool busy)
    {
        _busy = busy;
        SearchButton.IsEnabled = !busy;
        QueryBox.IsEnabled = !busy;
        ProviderCombo.IsEnabled = !busy;
        BrowserFallbackButton.IsEnabled = !busy;
    }
}
