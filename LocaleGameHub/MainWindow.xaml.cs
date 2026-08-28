using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LocaleGameHub.Models;
using LocaleGameHub.Services;
using Microsoft.Win32;

namespace LocaleGameHub;

public partial class MainWindow : Window
{
    private enum LibraryViewMode
    {
        All,
        Favorites,
        Developers
    }

    private readonly LibraryService _library = new();
    private readonly LocaleEmulatorService _locale;
    private readonly VndbService _vndb = new();
    private bool _startupLocaleCheckDone;
    private bool _developersSyncedThisSession;
    private bool _developerSyncInProgress;
    private LibraryViewMode _viewMode = LibraryViewMode.All;
    private string? _selectedDeveloperId;
    private int _currentPage = 1;

    public MainWindow()
    {
        InitializeComponent();
        WindowState = WindowState.Maximized;
        DarkTitleBarService.Apply(this);
        LocalizationService.SetLanguage(_library.Settings.Language);
        LocalizationService.Apply(this);
        _locale = new LocaleEmulatorService(_library);
        Loaded += MainWindow_Loaded;
        RefreshLibrary();
        RefreshLocaleBanner();
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Maximized;
        LocalizationService.Apply(this);
        UpdateTabVisuals();

        if (_startupLocaleCheckDone) return;
        _startupLocaleCheckDone = true;

        if (!HasValidLocaleEmulatorPath())
        {
            var settings = new SettingsWindow(_library, setupRequired: true) { Owner = this };
            if (settings.ShowDialog() == true)
            {
                LocalizationService.SetLanguage(_library.Settings.Language);
                LocalizationService.Apply(this);
                _currentPage = 1;
                RefreshLibrary();
            }
            RefreshLocaleBanner();
        }
    }

    private bool HasValidLocaleEmulatorPath()
    {
        var path = _library.Settings.LocaleEmulatorPath;
        return !string.IsNullOrWhiteSpace(path)
            && File.Exists(path)
            && string.Equals(Path.GetFileName(path), "LEProc.exe", StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshLibrary()
    {
        UpdateTabVisuals();
        UpdateDeveloperNavigation();

        if (_viewMode == LibraryViewMode.Developers && string.IsNullOrWhiteSpace(_selectedDeveloperId))
        {
            RefreshDeveloperCards();
            return;
        }

        RefreshGameCards();
    }

    private void RefreshGameCards()
    {
        var q = SearchBox?.Text?.Trim() ?? string.Empty;
        IEnumerable<GameEntry> games = _library.Games;

        if (_viewMode == LibraryViewMode.Favorites)
            games = games.Where(g => g.Favorite);
        else if (_viewMode == LibraryViewMode.Developers && !string.IsNullOrWhiteSpace(_selectedDeveloperId))
            games = games.Where(g => g.Developers.Any(d => string.Equals(d.Id, _selectedDeveloperId, StringComparison.OrdinalIgnoreCase)));

        if (!string.IsNullOrWhiteSpace(q))
        {
            games = games.Where(g => g.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                || g.ExePath.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        var filtered = games.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var pageSize = Math.Clamp(_library.Settings.PageSize, 10, 50);
        var totalPages = Math.Max(1, (int)Math.Ceiling(filtered.Count / (double)pageSize));
        _currentPage = Math.Clamp(_currentPage, 1, totalPages);

        GamesItems.ItemsSource = filtered
            .Skip((_currentPage - 1) * pageSize)
            .Take(pageSize)
            .Select(g => new GameCardViewModel { Game = g })
            .ToList();

        DevelopersItems.ItemsSource = null;
        GamesScroll.Visibility = Visibility.Visible;
        DevelopersScroll.Visibility = Visibility.Collapsed;

        CountText.Text = LocalizationService.IsSpanish
            ? $"{filtered.Count} juego{(filtered.Count == 1 ? string.Empty : "s")}" 
            : $"{filtered.Count} game{(filtered.Count == 1 ? string.Empty : "s")}";
        ApplyPagination(filtered.Count, totalPages);
        UpdateEmptyState(q, filtered.Count);

        Dispatcher.BeginInvoke(new Action(() =>
        {
            LocalizationService.Apply(this);
            UpdateTabVisuals();
            UpdateDeveloperNavigation();
        }));
    }

    private void RefreshDeveloperCards()
    {
        var q = SearchBox?.Text?.Trim() ?? string.Empty;
        var associatedIds = _library.Games
            .SelectMany(g => g.Developers)
            .Where(d => !string.IsNullOrWhiteSpace(d.Id))
            .GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);

        foreach (var pair in associatedIds)
            _library.EnsureDeveloper(pair.Key, pair.Value);
        if (associatedIds.Count > 0) _library.SaveDevelopers();

        var cards = _library.Developers
            .Where(d => associatedIds.ContainsKey(d.VndbId))
            .Where(d => string.IsNullOrWhiteSpace(q) || d.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(d => new DeveloperCardViewModel
            {
                Developer = d,
                GameCount = _library.Games.Count(g => g.Developers.Any(x => string.Equals(x.Id, d.VndbId, StringComparison.OrdinalIgnoreCase)))
            })
            .ToList();

        var pageSize = Math.Clamp(_library.Settings.PageSize, 10, 50);
        var totalPages = Math.Max(1, (int)Math.Ceiling(cards.Count / (double)pageSize));
        _currentPage = Math.Clamp(_currentPage, 1, totalPages);

        DevelopersItems.ItemsSource = cards.Skip((_currentPage - 1) * pageSize).Take(pageSize).ToList();
        GamesItems.ItemsSource = null;
        DevelopersScroll.Visibility = Visibility.Visible;
        GamesScroll.Visibility = Visibility.Collapsed;

        CountText.Text = LocalizationService.IsSpanish
            ? $"{cards.Count} developer{(cards.Count == 1 ? string.Empty : "s")}" 
            : $"{cards.Count} developer{(cards.Count == 1 ? string.Empty : "s")}";
        ApplyPagination(cards.Count, totalPages);
        UpdateEmptyState(q, cards.Count);
    }

    private void ApplyPagination(int itemCount, int totalPages)
    {
        PageText.Text = LocalizationService.IsSpanish
            ? $"Página {_currentPage} de {totalPages}"
            : $"Page {_currentPage} of {totalPages}";
        PrevPageButton.IsEnabled = _currentPage > 1;
        NextPageButton.IsEnabled = _currentPage < totalPages;
        PaginationPanel.Visibility = itemCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = itemCount == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateTabVisuals()
    {
        AllTabButton.Content = LocalizationService.Bi("Todos", "All games");
        FavoritesTabButton.Content = LocalizationService.Bi("Favoritos", "Favorites");
        DevelopersTabButton.Content = "Developers";

        AllTabButton.Tag = _viewMode == LibraryViewMode.All ? "active" : null;
        FavoritesTabButton.Tag = _viewMode == LibraryViewMode.Favorites ? "active" : null;
        DevelopersTabButton.Tag = _viewMode == LibraryViewMode.Developers ? "active" : null;
    }

    private void UpdateDeveloperNavigation()
    {
        var inDevelopers = _viewMode == LibraryViewMode.Developers;
        DeveloperNavPanel.Visibility = inDevelopers ? Visibility.Visible : Visibility.Collapsed;
        if (!inDevelopers)
        {
            SearchBox.ToolTip = LocalizationService.Bi("Buscar por nombre o ruta", "Search by name or path");
            return;
        }

        var selected = string.IsNullOrWhiteSpace(_selectedDeveloperId)
            ? null
            : _library.Developers.FirstOrDefault(d => string.Equals(d.VndbId, _selectedDeveloperId, StringComparison.OrdinalIgnoreCase));

        DeveloperBackButton.Visibility = selected is null ? Visibility.Collapsed : Visibility.Visible;
        RefreshDevelopersButton.Visibility = selected is null ? Visibility.Visible : Visibility.Collapsed;
        DeveloperBackButton.Content = LocalizationService.Bi("← Developers", "← Developers");

        if (selected is null)
        {
            DeveloperPathText.Text = "Developers";
            DeveloperHintText.Text = _developerSyncInProgress
                ? LocalizationService.Bi("Consultando VNDB…", "Querying VNDB…")
                : LocalizationService.Bi("Selecciona un developer para ver sus juegos.", "Select a developer to see its games.");
            RefreshDevelopersButton.Content = LocalizationService.Bi("↻ Actualizar desde VNDB", "↻ Refresh from VNDB");
            SearchBox.ToolTip = LocalizationService.Bi("Buscar developer", "Search developer");
        }
        else
        {
            DeveloperPathText.Text = $"Developers  ›  {selected.Name}";
            DeveloperHintText.Text = LocalizationService.Bi("Mostrando los juegos asociados a este developer en VNDB.", "Showing games associated with this developer on VNDB.");
            SearchBox.ToolTip = LocalizationService.Bi("Buscar juegos de este developer", "Search this developer's games");
        }
    }

    private void UpdateEmptyState(string query, int itemCount)
    {
        if (itemCount > 0)
        {
            EmptyState.Visibility = Visibility.Collapsed;
            return;
        }

        EmptyState.Visibility = Visibility.Visible;

        if (!string.IsNullOrWhiteSpace(query))
        {
            EmptyIconText.Text = "⌕";
            EmptyTitleText.Text = _viewMode == LibraryViewMode.Developers && string.IsNullOrWhiteSpace(_selectedDeveloperId)
                ? LocalizationService.Bi("No se encontraron developers", "No developers found")
                : LocalizationService.Bi("No se encontraron juegos", "No games found");
            EmptyDescriptionText.Text = LocalizationService.Bi("Prueba con otro nombre.", "Try another name.");
            return;
        }

        if (_viewMode == LibraryViewMode.Developers && string.IsNullOrWhiteSpace(_selectedDeveloperId))
        {
            EmptyIconText.Text = "◆";
            EmptyTitleText.Text = LocalizationService.Bi("Aún no hay developers detectados", "No developers detected yet");
            EmptyDescriptionText.Text = LocalizationService.Bi(
                "Configura el ID de VNDB en tus juegos y VNAR agrupará automáticamente sus developers aquí.",
                "Configure VNDB IDs for your games and VNAR will automatically group their developers here.");
            return;
        }

        if (_viewMode == LibraryViewMode.Favorites)
        {
            EmptyIconText.Text = "★";
            EmptyTitleText.Text = LocalizationService.Bi("Aún no tienes favoritos", "No favorites yet");
            EmptyDescriptionText.Text = LocalizationService.Bi(
                "Marca ★ Favorito en la configuración de un juego y aparecerá aquí.",
                "Mark ★ Favorite in a game's settings and it will appear here.");
            return;
        }

        EmptyIconText.Text = "🎮";
        EmptyTitleText.Text = LocalizationService.Bi("Tu biblioteca está vacía", "Your library is empty");
        EmptyDescriptionText.Text = LocalizationService.Bi(
            "Arrastra aquí un .exe o una carpeta de juegos, añade un ejecutable o escanea una biblioteca completa.",
            "Drop an .exe or a game folder here, add an executable, or scan a complete library.");
    }

    private void AllTab_Click(object sender, RoutedEventArgs e)
    {
        if (_viewMode == LibraryViewMode.All) return;
        _viewMode = LibraryViewMode.All;
        _selectedDeveloperId = null;
        _currentPage = 1;
        SearchBox.Text = string.Empty;
        RefreshLibrary();
    }

    private void FavoritesTab_Click(object sender, RoutedEventArgs e)
    {
        if (_viewMode == LibraryViewMode.Favorites) return;
        _viewMode = LibraryViewMode.Favorites;
        _selectedDeveloperId = null;
        _currentPage = 1;
        SearchBox.Text = string.Empty;
        RefreshLibrary();
    }

    private async void DevelopersTab_Click(object sender, RoutedEventArgs e)
    {
        _viewMode = LibraryViewMode.Developers;
        _selectedDeveloperId = null;
        _currentPage = 1;
        SearchBox.Text = string.Empty;
        UpdateTabVisuals();
        UpdateDeveloperNavigation();
        if (!_developersSyncedThisSession)
            await SyncDevelopersAsync(force: false);
        RefreshLibrary();
    }

    private async void RefreshDevelopers_Click(object sender, RoutedEventArgs e)
        => await SyncDevelopersAsync(force: true);

    private async Task SyncDevelopersAsync(bool force)
    {
        if (_developerSyncInProgress) return;
        var games = _library.Games.Where(g => !string.IsNullOrWhiteSpace(VndbService.NormalizeId(g.VndbId))).ToList();
        if (games.Count == 0)
        {
            _developersSyncedThisSession = true;
            RefreshLibrary();
            return;
        }

        if (!force && _developersSyncedThisSession) return;

        _developerSyncInProgress = true;
        RefreshDevelopersButton.IsEnabled = false;
        UpdateDeveloperNavigation();
        try
        {
            var map = await _vndb.GetDevelopersForVisualNovelsAsync(games.Select(g => g.VndbId));
            foreach (var game in games)
            {
                var id = VndbService.NormalizeId(game.VndbId);
                if (!map.TryGetValue(id, out var refs)) continue;
                game.Developers = refs.Select(d => new VndbDeveloperRef { Id = d.Id, Name = d.Name }).ToList();
                foreach (var developer in refs)
                    _library.EnsureDeveloper(developer.Id, developer.Name);
            }
            _library.SaveGames();
            _library.SaveDevelopers();
            _developersSyncedThisSession = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                LocalizationService.Bi("No se pudieron actualizar los developers desde VNDB. Se mostrarán los datos almacenados.\n\n", "Developers could not be refreshed from VNDB. Cached data will be shown.\n\n") + ex.Message,
                "VNDB",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            _developerSyncInProgress = false;
            RefreshDevelopersButton.IsEnabled = true;
            RefreshLibrary();
        }
    }

    private void DeveloperCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.ClickCount != 1) return;
        if ((sender as FrameworkElement)?.Tag is not DeveloperEntry developer) return;
        _selectedDeveloperId = developer.VndbId;
        _currentPage = 1;
        SearchBox.Text = string.Empty;
        RefreshLibrary();
    }

    private void DeveloperBack_Click(object sender, RoutedEventArgs e)
    {
        _selectedDeveloperId = null;
        _currentPage = 1;
        SearchBox.Text = string.Empty;
        RefreshLibrary();
    }

    private void DeveloperCard_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: DeveloperEntry developer } card) return;
        var menu = card.ContextMenu ?? new ContextMenu();
        menu.Items.Clear();
        var cover = new MenuItem
        {
            Header = LocalizationService.Bi("Ajustar portada…", "Adjust cover…"),
            Tag = developer
        };
        cover.Click += DeveloperCover_Click;
        menu.Items.Add(cover);
        card.ContextMenu = menu;
    }

    private void DeveloperCover_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not DeveloperEntry developer) return;
        var dialog = new DeveloperCoverWindow(_library, developer) { Owner = this };
        if (dialog.ShowDialog() == true) RefreshLibrary();
    }

    private void RefreshLocaleBanner()
    {
        var path = _library.Settings.LocaleEmulatorPath;
        if (!HasValidLocaleEmulatorPath())
        {
            LocaleBanner.Visibility = Visibility.Visible;
            LocaleBannerText.Text = LocalizationService.Bi(
                "Locale Emulator aún no está configurado o LEProc.exe ya no existe en la ruta guardada.",
                "Locale Emulator is not configured yet or LEProc.exe no longer exists at the saved path.");
            return;
        }

        try { LocaleEmulatorInstallerService.EnsureDefaultConfig(path); } catch { }

        _locale.AutoAssignJapaneseProfiles();
        var missingNormal = string.IsNullOrWhiteSpace(_library.Settings.NormalProfileGuid);
        var missingAdmin = string.IsNullOrWhiteSpace(_library.Settings.AdminProfileGuid);
        if (missingNormal || missingAdmin)
        {
            LocaleBanner.Visibility = Visibility.Visible;
            LocaleBannerText.Text = LocalizationService.Bi(
                "Encontré Locale Emulator, pero faltan perfiles japoneses normal/admin en LEConfig.xml. Revisa Ajustes.",
                "Locale Emulator was found, but normal/admin Japanese profiles are missing from LEConfig.xml. Check Settings.");
        }
        else
        {
            LocaleBanner.Visibility = Visibility.Collapsed;
        }
    }

    private void AddExe_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = LocalizationService.Bi("Ejecutables (*.exe)|*.exe", "Executables (*.exe)|*.exe"),
            Multiselect = true,
            Title = LocalizationService.Bi("Añadir juegos", "Add games")
        };
        if (dialog.ShowDialog(this) != true) return;
        foreach (var exe in dialog.FileNames) AddExecutable(exe, openEditor: dialog.FileNames.Length == 1);
        RefreshLibrary();
    }

    private void AddExecutable(string exe, bool openEditor = false)
    {
        if (!File.Exists(exe) || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return;
        if (_library.ContainsExe(exe)) return;

        var game = new GameEntry
        {
            Name = ScannerService.InferNameFromExe(exe),
            ExePath = Path.GetFullPath(exe)
        };
        _library.Add(game);
        if (openEditor)
        {
            var editor = new GameEditorWindow(_library, game) { Owner = this };
            editor.ShowDialog();
        }
    }

    private void ScanFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = LocalizationService.Bi("Selecciona la carpeta raíz de tus juegos", "Select the root folder of your games")
        };
        if (dialog.ShowDialog(this) == true) ScanAndImport(dialog.FolderName);
    }

    private void ScanAndImport(string folder)
    {
        var candidates = ScannerService.Scan(folder).Where(c => !_library.ContainsExe(c.ExePath)).ToList();
        if (candidates.Count == 0)
        {
            MessageBox.Show(this,
                LocalizationService.Bi("No encontré ejecutables nuevos que parezcan juegos en esa carpeta.", "I couldn't find any new executables that look like games in that folder."),
                LocalizationService.Bi("Escaneo", "Scan"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var scan = new ScanWindow(candidates) { Owner = this };
        if (scan.ShowDialog() != true) return;
        foreach (var c in scan.SelectedCandidates)
            _library.Add(new GameEntry { Name = c.Name, ExePath = c.ExePath });
        RefreshLibrary();
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is GameEntry game) LaunchGame(game);
    }

    private void Cover_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.ClickCount != 2) return;
        if ((sender as FrameworkElement)?.Tag is not GameEntry game) return;
        e.Handled = true;
        LaunchGame(game);
    }

    private void LaunchGame(GameEntry game)
    {
        try
        {
            _locale.Launch(game);
            game.LastPlayedUtc = DateTime.UtcNow;
            _library.SaveGames();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message,
                LocalizationService.Bi("No se pudo iniciar", "Could not start"),
                MessageBoxButton.OK, MessageBoxImage.Error);
            RefreshLocaleBanner();

            if (!HasValidLocaleEmulatorPath())
            {
                var settings = new SettingsWindow(_library, setupRequired: true) { Owner = this };
                if (settings.ShowDialog() == true)
                {
                    LocalizationService.SetLanguage(_library.Settings.Language);
                    LocalizationService.Apply(this);
                    _currentPage = 1;
                    RefreshLibrary();
                }
                RefreshLocaleBanner();
            }
        }
    }

    private void GameCard_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: GameEntry game } card) return;
        var menu = card.ContextMenu ?? new ContextMenu();
        menu.Items.Clear();

        var play = new MenuItem { Header = LocalizationService.Bi("Jugar", "Play"), Tag = game };
        play.Click += ContextPlay_Click;
        var settings = new MenuItem { Header = LocalizationService.Bi("Ajustes", "Settings"), Tag = game };
        settings.Click += ContextSettings_Click;
        var shortcut = new MenuItem { Header = LocalizationService.Bi("Crear acceso directo…", "Create shortcut…"), Tag = game };
        shortcut.Click += ContextShortcut_Click;

        menu.Items.Add(play);
        menu.Items.Add(settings);
        menu.Items.Add(new Separator());
        menu.Items.Add(shortcut);
        card.ContextMenu = menu;
    }

    private void ContextPlay_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is GameEntry game) LaunchGame(game);
    }

    private void ContextSettings_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is GameEntry game) OpenGameEditor(game);
    }

    private void ContextShortcut_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is GameEntry game) OpenShortcutWindow(game);
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is GameEntry game) OpenGameEditor(game);
    }

    private void OpenGameEditor(GameEntry game)
    {
        var editor = new GameEditorWindow(_library, game) { Owner = this };
        editor.ShowDialog();
        _developersSyncedThisSession = false;
        RefreshLibrary();
    }

    private void OpenShortcutWindow(GameEntry game)
    {
        var dialog = new ShortcutWindow(game) { Owner = this };
        dialog.ShowDialog();
    }

    private void AdminToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        if (sender is CheckBox checkBox && checkBox.Tag is GameEntry game)
            game.RunAsAdmin = checkBox.IsChecked == true;
        _library.SaveGames();
    }

    private void FavoriteStar_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not GameEntry game) return;
        game.Favorite = !game.Favorite;
        _library.SaveGames();
        RefreshLibrary();
        e.Handled = true;
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var previousPageSize = _library.Settings.PageSize;
        var previousLanguage = _library.Settings.Language;
        var settings = new SettingsWindow(_library) { Owner = this };
        if (settings.ShowDialog() == true)
        {
            if (previousPageSize != _library.Settings.PageSize) _currentPage = 1;
            if (!string.Equals(previousLanguage, _library.Settings.Language, StringComparison.OrdinalIgnoreCase))
            {
                LocalizationService.SetLanguage(_library.Settings.Language);
                LocalizationService.Apply(this);
            }
            RefreshLibrary();
        }
        RefreshLocaleBanner();
    }

    private void PrevPage_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage <= 1) return;
        _currentPage--;
        RefreshLibrary();
    }

    private void NextPage_Click(object sender, RoutedEventArgs e)
    {
        _currentPage++;
        RefreshLibrary();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _currentPage = 1;
        RefreshLibrary();
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
        foreach (var path in paths)
        {
            if (File.Exists(path) && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) AddExecutable(path);
            else if (Directory.Exists(path)) ScanAndImport(path);
        }
        RefreshLibrary();
    }
}
