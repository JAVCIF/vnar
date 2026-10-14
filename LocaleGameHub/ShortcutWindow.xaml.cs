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
    private readonly LibraryService? _library;
    private readonly System.Collections.ObjectModel.ObservableCollection<ShortcutIconOption> _icons;

    public ShortcutWindow(GameEntry game, LibraryService? library = null)
    {
        InitializeComponent();
        DarkTitleBarService.Apply(this);
        LocalizationService.Apply(this);
        _game = game;
        _library = library;

        KindTitleText.Text = LocalizationService.Bi("Tipo de acceso directo", "Shortcut type");
        ModernKindRadio.Content = LocalizationService.Bi("Moderno (recomendado)", "Modern (recommended)");
        ClassicKindRadio.Content = LocalizationService.Bi("Clásico", "Classic");
        var preferClassic = string.Equals(library?.Settings.ShortcutKind, nameof(ShortcutKind.Classic), StringComparison.OrdinalIgnoreCase);
        (preferClassic ? ClassicKindRadio : ModernKindRadio).IsChecked = true;

        NameBox.Text = game.Name;
        DestinationBox.Text = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        _icons = new(ExecutableIconService.FindChoices(game.ExePath));
        IconsList.ItemsSource = _icons;
        CustomIconButton.Content = LocalizationService.Bi("Elegir .ico…", "Choose .ico…");

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

    private void CustomIcon_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = LocalizationService.Bi("Elige un icono", "Choose an icon"),
            Filter = LocalizationService.Bi("Iconos (*.ico)|*.ico", "Icons (*.ico)|*.ico")
        };
        if (dialog.ShowDialog(this) != true) return;

        var option = ExecutableIconService.CreateCustomIconOption(dialog.FileName);
        if (option is null)
        {
            MessageBox.Show(this,
                LocalizationService.Bi(
                    "Ese archivo no es un icono de Windows válido. Si es un PNG renombrado, conviértelo primero a .ico.",
                    "That file is not a valid Windows icon. If it is a renamed PNG, convert it to .ico first."),
                LocalizationService.Bi("Icono personalizado", "Custom icon"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // Only one custom choice at a time: picking another replaces it.
        foreach (var previous in _icons.Where(i => i.IsCustom).ToList())
            _icons.Remove(previous);
        _icons.Insert(0, option);
        IconsList.SelectedItem = option;
        IconsList.ScrollIntoView(option);
    }

    private ShortcutKind SelectedKind => ClassicKindRadio.IsChecked == true ? ShortcutKind.Classic : ShortcutKind.Modern;

    private void ShortcutKind_Checked(object sender, RoutedEventArgs e)
    {
        KindHintText.Text = SelectedKind == ShortcutKind.Modern
            ? LocalizationService.Bi(
                "Funciona como los accesos de Steam o Playnite (vnar://). VNAR mantiene su propio nombre e icono en Windows. No se puede anclar a la barra de tareas.",
                "Works like Steam or Playnite shortcuts (vnar://). VNAR keeps its own name and icon in Windows. Cannot be pinned to the taskbar.")
            : LocalizationService.Bi(
                "Acceso .lnk tradicional: se puede anclar a la barra de tareas, pero Windows puede mostrar a VNAR con el nombre o icono del último juego abierto.",
                "Traditional .lnk shortcut: can be pinned to the taskbar, but Windows may show VNAR with the name or icon of the last game opened.");
    }

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
        var kind = SelectedKind;
        var safeName = string.Join("_", name.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim().TrimEnd('.');
        var expectedPath = Path.Combine(folder, safeName + ShortcutService.ExtensionFor(kind));
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
            // A custom .ico is copied into VNAR's data folder first, so the shortcut
            // does not lose its icon if the original file is moved or deleted.
            var stored = icon.IsCustom ? IconStore.StoreCustomIcon(icon.IconPath, _game.Id) : (StoredIcon?)null;
            string created;
            try
            {
                created = ShortcutService.CreateShortcut(_game, name, folder, stored?.Path ?? icon.IconPath, kind);
            }
            catch
            {
                // Do not leave an icon behind for a shortcut that was never created.
                if (stored is { } copy) IconStore.DiscardIfCreated(copy);
                throw;
            }

            // Remember the choice for next time (only once creation succeeded).
            if (_library is not null && !string.Equals(_library.Settings.ShortcutKind, kind.ToString(), StringComparison.Ordinal))
            {
                _library.Settings.ShortcutKind = kind.ToString();
                try { _library.SaveSettings(); } catch { /* preference only */ }
            }
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
