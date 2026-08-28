using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using LocaleGameHub.Models;
using LocaleGameHub.Services;
using Microsoft.Win32;

namespace LocaleGameHub;

public partial class SettingsWindow : Window
{
    private readonly LibraryService _library;
    private readonly bool _setupRequired;
    private List<ProfileChoice> _profiles = [];
    private bool _busy;
    private string _selectedLanguage = "en";

    public SettingsWindow(LibraryService library, bool setupRequired = false)
    {
        InitializeComponent();
        DarkTitleBarService.Apply(this);
        _library = library;
        _setupRequired = setupRequired;
        FirstRunPanel.Visibility = setupRequired ? Visibility.Visible : Visibility.Collapsed;
        Title = setupRequired ? "Configurar Locale Emulator · VNAR" : "Ajustes · VNAR";
        LePathBox.Text = _library.Settings.LocaleEmulatorPath;
        SerpApiKeyBox.Password = _library.Settings.SerpApiKey;

        PageSizeCombo.ItemsSource = Enumerable.Range(10, 41).ToList();

        SelectLanguage(_library.Settings.Language);
        PageSizeCombo.SelectedItem = Math.Clamp(_library.Settings.PageSize, 10, 50);
        LocalizationService.Apply(this);
        Loaded += (_, _) =>
        {
            ReloadProfiles();
            LocalizationService.Apply(this);
        };
    }

    private void SelectLanguage(string? language)
    {
        _selectedLanguage = string.Equals(language, "es", StringComparison.OrdinalIgnoreCase) ? "es" : "en";
        UpdateLanguageButtons();
    }

    private string SelectedLanguage() => _selectedLanguage;

    private void SpanishLanguage_Click(object sender, RoutedEventArgs e)
    {
        _selectedLanguage = "es";
        UpdateLanguageButtons();
    }

    private void EnglishLanguage_Click(object sender, RoutedEventArgs e)
    {
        _selectedLanguage = "en";
        UpdateLanguageButtons();
    }

    private void UpdateLanguageButtons()
    {
        SpanishLanguageButton.Tag = _selectedLanguage == "es" ? "active" : null;
        EnglishLanguageButton.Tag = _selectedLanguage == "en" ? "active" : null;
    }

    private void BrowseLe_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = LocalizationService.Bi(
                "Locale Emulator (LEProc.exe)|LEProc.exe|Ejecutables (*.exe)|*.exe",
                "Locale Emulator (LEProc.exe)|LEProc.exe|Executables (*.exe)|*.exe"),
            FileName = "LEProc.exe",
            Title = LocalizationService.Bi("Selecciona LEProc.exe", "Select LEProc.exe")
        };
        if (dialog.ShowDialog(this) == true)
        {
            LePathBox.Text = dialog.FileName;
            try
            {
                LocaleEmulatorInstallerService.EnsureDefaultConfig(dialog.FileName);
            }
            catch
            {
                // An existing installation may be read-only. ReloadProfiles will explain the problem.
            }
            ReloadProfiles();
        }
    }

    private async void DownloadLe_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;

        var folderDialog = new OpenFolderDialog
        {
            Title = LocalizationService.Bi("Elige dónde guardar Locale Emulator", "Choose where to store Locale Emulator"),
            Multiselect = false
        };

        if (folderDialog.ShowDialog(this) != true) return;

        SetBusy(true);
        try
        {
            var progress = new Progress<string>(message => StatusText.Text = message);
            var installer = new LocaleEmulatorInstallerService();
            var result = await installer.DownloadLatestAsync(folderDialog.FolderName, progress);

            LePathBox.Text = result.LeProcPath;
            ReloadProfiles();
            StatusText.Text = LocalizationService.IsSpanish
                ? $"✓ Locale Emulator {result.VersionTag} descargado y configurado. Encontré LEProc.exe en:\n{result.LeProcPath}"
                : $"✓ Locale Emulator {result.VersionTag} downloaded and configured. LEProc.exe was found at:\n{result.LeProcPath}";
        }
        catch (Exception ex)
        {
            StatusText.Text = LocalizationService.Bi("No se pudo completar la descarga.", "The download could not be completed.");
            MessageBox.Show(this,
                LocalizationService.IsSpanish
                    ? $"No pude descargar/configurar Locale Emulator.\n\n{ex.Message}"
                    : $"Could not download/configure Locale Emulator.\n\n{ex.Message}",
                "Locale Emulator",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OpenRepo_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = LocaleEmulatorInstallerService.RepositoryUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "GitHub", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenSerpApi_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://serpapi.com/",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "SerpApi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ReloadProfiles_Click(object sender, RoutedEventArgs e) => ReloadProfiles();

    private void ReloadProfiles()
    {
        _library.Settings.LocaleEmulatorPath = LePathBox.Text.Trim();
        var path = _library.Settings.LocaleEmulatorPath;

        if (File.Exists(path))
        {
            try
            {
                LocaleEmulatorInstallerService.EnsureDefaultConfig(path);
            }
            catch
            {
                // The read operation below provides the user-facing state.
            }
        }

        var service = new LocaleEmulatorService(_library);
        var profiles = service.ReadProfiles();

        _profiles = profiles.Select(p => new ProfileChoice(p.Name, p.Guid, p.Location, p.RunAsAdmin)).ToList();
        NormalProfileCombo.ItemsSource = _profiles.Where(p => !p.RunAsAdmin).ToList();
        AdminProfileCombo.ItemsSource = _profiles.Where(p => p.RunAsAdmin).ToList();

        ProfileChoice? normal = null;
        ProfileChoice? admin = null;

        if (!string.IsNullOrWhiteSpace(_library.Settings.NormalProfileGuid))
            normal = _profiles.FirstOrDefault(p => p.Guid == _library.Settings.NormalProfileGuid);
        normal ??= _profiles.FirstOrDefault(p => !p.RunAsAdmin && p.Location.Equals("ja-JP", StringComparison.OrdinalIgnoreCase));
        normal ??= _profiles.FirstOrDefault(p => !p.RunAsAdmin && p.Name.Contains("Japanese", StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(_library.Settings.AdminProfileGuid))
            admin = _profiles.FirstOrDefault(p => p.Guid == _library.Settings.AdminProfileGuid);
        admin ??= _profiles.FirstOrDefault(p => p.RunAsAdmin && p.Location.Equals("ja-JP", StringComparison.OrdinalIgnoreCase));
        admin ??= _profiles.FirstOrDefault(p => p.RunAsAdmin && p.Name.Contains("Japanese", StringComparison.OrdinalIgnoreCase));

        NormalProfileCombo.SelectedItem = normal;
        AdminProfileCombo.SelectedItem = admin;

        if (!File.Exists(path))
            StatusText.Text = _setupRequired
                ? LocalizationService.Bi("Selecciona un LEProc.exe existente o usa “Descargar / configurar LE”.", "Select an existing LEProc.exe or use “Download / configure LE”.")
                : LocalizationService.Bi("No encuentro LEProc.exe en la ruta guardada. Selecciónalo de nuevo o descarga una copia.", "LEProc.exe was not found at the saved path. Select it again or download a copy.");
        else if (_profiles.Count == 0)
            StatusText.Text = LocalizationService.Bi(
                "LEProc.exe existe, pero no pude leer LEConfig.xml. Comprueba que la carpeta sea escribible y vuelve a leer los perfiles.",
                "LEProc.exe exists, but LEConfig.xml could not be read. Make sure the folder is writable and read the profiles again.");
        else
            StatusText.Text = LocalizationService.IsSpanish
                ? $"✓ Encontré {_profiles.Count} perfil(es). El Hub usará -runas + GUID, igual que Run in Japanese del menú contextual."
                : $"✓ Found {_profiles.Count} profile(s). VNAR will use -runas + GUID, just like Run in Japanese from the context menu.";
    }

    private void ProfileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Selection is persisted only when Save is pressed.
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var path = LePathBox.Text.Trim();
        if (!File.Exists(path) || !string.Equals(Path.GetFileName(path), "LEProc.exe", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this,
                LocalizationService.Bi("Selecciona un LEProc.exe válido.", "Select a valid LEProc.exe."),
                LocalizationService.Bi("Ajustes", "Settings"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (NormalProfileCombo.SelectedItem is not ProfileChoice normal)
        {
            MessageBox.Show(this,
                LocalizationService.Bi("Selecciona el perfil japonés normal.", "Select the normal Japanese profile."),
                LocalizationService.Bi("Ajustes", "Settings"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (AdminProfileCombo.SelectedItem is not ProfileChoice admin)
        {
            MessageBox.Show(this,
                LocalizationService.Bi("Selecciona el perfil japonés con administrador.", "Select the administrator Japanese profile."),
                LocalizationService.Bi("Ajustes", "Settings"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (PageSizeCombo.SelectedItem is not int pageSize || pageSize < 10 || pageSize > 50)
        {
            MessageBox.Show(this,
                LocalizationService.Bi("Selecciona entre 10 y 50 juegos por página.", "Select between 10 and 50 games per page."),
                LocalizationService.Bi("Ajustes", "Settings"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _library.Settings.LocaleEmulatorPath = path;
        _library.Settings.NormalProfileGuid = normal.Guid;
        _library.Settings.NormalProfileName = normal.Name;
        _library.Settings.AdminProfileGuid = admin.Guid;
        _library.Settings.AdminProfileName = admin.Name;
        _library.Settings.SerpApiKey = SerpApiKeyBox.Password.Trim();
        _library.Settings.Language = SelectedLanguage();
        _library.Settings.PageSize = pageSize;
        _library.SaveSettings();
        LocalizationService.SetLanguage(_library.Settings.Language);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _library.Load(); // discard temporary path changes made during profile probing
        DialogResult = false;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        BrowseButton.IsEnabled = !busy;
        DownloadButton.IsEnabled = !busy;
        ReloadButton.IsEnabled = !busy;
        OpenRepoButton.IsEnabled = !busy;
        OpenSerpApiButton.IsEnabled = !busy;
        SerpApiKeyBox.IsEnabled = !busy;
        SaveButton.IsEnabled = !busy;
        CancelButton.IsEnabled = !busy;
        LePathBox.IsEnabled = !busy;
        NormalProfileCombo.IsEnabled = !busy;
        AdminProfileCombo.IsEnabled = !busy;
        SpanishLanguageButton.IsEnabled = !busy;
        EnglishLanguageButton.IsEnabled = !busy;
        PageSizeCombo.IsEnabled = !busy;
    }

    private sealed record ProfileChoice(string Name, string Guid, string Location, bool RunAsAdmin)
    {
        public string Display => string.IsNullOrWhiteSpace(Location) ? Name : $"{Name}  [{Location}]";
    }
}
