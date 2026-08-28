using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using LocaleGameHub.Models;
using LocaleGameHub.Services;
using Microsoft.Win32;

namespace LocaleGameHub;

public partial class ShortcutWindow : Window
{
    private readonly GameEntry _game;
    private readonly IReadOnlyList<ShortcutIconOption> _icons;

    public ShortcutWindow(GameEntry game)
    {
        InitializeComponent();
        DarkTitleBarService.Apply(this);
        LocalizationService.Apply(this);
        _game = game;

        NameBox.Text = game.Name;
        DestinationBox.Text = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        _icons = ExecutableIconService.FindChoices(game.ExePath);
        IconsList.ItemsSource = _icons;

        var preferred = _icons.FirstOrDefault(i => !i.IsGeneric) ?? _icons.FirstOrDefault();
        if (preferred is not null) IconsList.SelectedItem = preferred;
        UpdateStatus();
    }

    private void BrowseDestination_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = LocalizationService.Bi("Elige dónde crear el acceso directo", "Choose where to create the shortcut"),
            InitialDirectory = Directory.Exists(DestinationBox.Text) ? DestinationBox.Text : null
        };
        if (dialog.ShowDialog(this) == true)
            DestinationBox.Text = dialog.FolderName;
    }

    private void IconsList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateStatus();

    private void UpdateStatus()
    {
        if (IconsList.SelectedItem is not ShortcutIconOption icon)
        {
            StatusText.Text = LocalizationService.Bi("Selecciona un icono.", "Select an icon.");
            return;
        }

        StatusText.Text = icon.IsGeneric
            ? LocalizationService.Bi("Se usará el icono genérico de VNAR.", "The generic VNAR icon will be used.")
            : LocalizationService.IsSpanish
                ? $"Icono tomado de: {Path.GetFileName(icon.IconPath)}"
                : $"Icon from: {Path.GetFileName(icon.IconPath)}";
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (IconsList.SelectedItem is not ShortcutIconOption icon)
        {
            MessageBox.Show(this,
                LocalizationService.Bi("Selecciona un icono.", "Select an icon."),
                LocalizationService.Bi("Acceso directo", "Shortcut"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var name = NameBox.Text.Trim();
        var folder = DestinationBox.Text.Trim();
        var safeName = string.Join("_", name.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim().TrimEnd('.');
        var expectedPath = Path.Combine(folder, safeName + ".lnk");
        if (File.Exists(expectedPath))
        {
            var overwrite = MessageBox.Show(this,
                LocalizationService.Bi("Ya existe un acceso directo con ese nombre. ¿Reemplazarlo?", "A shortcut with that name already exists. Replace it?"),
                LocalizationService.Bi("Acceso directo", "Shortcut"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (overwrite != MessageBoxResult.Yes) return;
        }

        try
        {
            var created = ShortcutService.CreateShortcut(_game, name, folder, icon.IconPath);
            StatusText.Text = LocalizationService.IsSpanish
                ? $"✓ Creado: {created}"
                : $"✓ Created: {created}";

            var openFolder = MessageBox.Show(this,
                LocalizationService.Bi("Acceso directo creado correctamente. ¿Abrir la carpeta de destino?", "Shortcut created successfully. Open the destination folder?"),
                LocalizationService.Bi("Acceso directo creado", "Shortcut created"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (openFolder == MessageBoxResult.Yes)
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{created}\"") { UseShellExecute = true });

            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                ex.Message,
                LocalizationService.Bi("No se pudo crear el acceso directo", "Could not create shortcut"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
