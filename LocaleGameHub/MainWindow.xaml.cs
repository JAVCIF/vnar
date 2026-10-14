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
        // Before the window is shown this is the system DPI; Loaded/OnDpiChanged
        // correct it for the monitor the window actually lands on.
        CoverImageLoader.DpiScale = System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX;
        RefreshLibrary();
        RefreshLocaleBanner();
    }

    protected override void OnDpiChanged(System.Windows.DpiScale oldDpi, System.Windows.DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        ApplyCoverDpi(newDpi.DpiScaleX);
    }

    /// <summary>Re-decodes visible covers when the window moves to a monitor with another scale.</summary>
    private void ApplyCoverDpi(double scale)
    {
        if (Math.Abs(CoverImageLoader.DpiScale - scale) < 0.001) return;
        CoverImageLoader.DpiScale = scale;
        RefreshLibrary();
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Maximized;
        ApplyCoverDpi(System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX);
        LocalizationService.Apply(this);
        UpdateTabVisuals();

        if (_startupLocaleCheckDone) return;
        _startupLocaleCheckDone = true;

        var dataWasRecovered = ShowStorageRecoveryNotices();
        // After a recovery the library may be missing entries, and their covers would
        // look unused. Only tidy the covers folder when the data loaded cleanly.
        if (!dataWasRecovered) StartCoverCleanup();

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

    /// <summary>Called when another VNAR launch asks this instance to show itself.</summary>
    public void BringToFront()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Maximized;

        Activate();
        // Topmost toggle: reliable way to raise a WPF window over the foreground app.
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void StartCoverCleanup()
    {
        // No library.json at all = fresh install or a file deleted by hand: nothing can
        // be judged unused. An existing but empty library means every game was
        // removed, and then every remaining cover really is unused (it still goes to
        // _trash first, recoverable for 30 days).
        if (!_library.LibraryFileExisted) return;

        static bool HasEditSource(Models.CoverEditState? edit)
            => !string.IsNullOrWhiteSpace(edit?.SourcePath) && File.Exists(edit.SourcePath);

        // Snapshot on the UI thread; the file work runs in the background.
        var owners = _library.Games
            .Select(g => new CoverOwner(g.Id, [g.CoverPath, g.CoverEdit?.SourcePath ?? string.Empty], HasEditSource(g.CoverEdit)))
            .Concat(_library.Developers
                .Select(d => new CoverOwner(d.LocalId, [d.CoverPath, d.CoverEdit?.SourcePath ?? string.Empty], HasEditSource(d.CoverEdit))))
            .ToList();
        var coversDir = AppDataPaths.CoversDir;
        var iconsDir = AppDataPaths.IconsDir;
        var gameIds = _library.Games.Select(g => g.Id).ToList();

        _ = Task.Run(() =>
        {
            try
            {
                CoverCleanupService.Run(coversDir, owners, DateTime.UtcNow);
                // Shortcut icons: only those of removed games (see RunOrphansOnly).
                CoverCleanupService.RunOrphansOnly(iconsDir, gameIds, DateTime.UtcNow);
            }
            catch
            {
                // Housekeeping must never disturb the user.
            }
        });
    }

    /// <summary>Shows pending data-recovery notices. Returns true if there were any.</summary>
    private bool ShowStorageRecoveryNotices()
    {
        var recoveries = JsonStore.DrainRecoveries(AppDataPaths.Root);
        if (recoveries.Count == 0) return false;

        var parts = recoveries.Select(r => r.Outcome == StorageRecoveryOutcome.RestoredFromBackup
            ? (LocalizationService.IsSpanish
                ? $"• {r.FileName} estaba dañado. Se restauró la versión anterior guardada automáticamente; puede faltar el último cambio.\n  Copia del archivo dañado:\n  {r.QuarantinedPath}"
                : $"• {r.FileName} was damaged. The automatically saved previous version was restored; the most recent change may be missing.\n  Copy of the damaged file:\n  {r.QuarantinedPath}")
            : (LocalizationService.IsSpanish
                ? $"• {r.FileName} estaba dañado y no había una copia anterior válida, así que VNAR empezó sin esos datos. El archivo original no se borró:\n  {r.QuarantinedPath}"
                : $"• {r.FileName} was damaged and no valid previous copy existed, so VNAR started without that data. The original file was not deleted:\n  {r.QuarantinedPath}"));

        MessageBox.Show(this,
            string.Join("\n\n", parts),
            LocalizationService.Bi("VNAR - Datos recuperados", "VNAR - Data recovered"),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        return true;
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

        var gameCards = filtered
            .Skip((_currentPage - 1) * pageSize)
            .Take(pageSize)
            .Select(g => new GameCardViewModel { Game = g })
            .ToList();
        GamesItems.ItemsSource = gameCards;
        StartCoverLoads(gameCards);

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

    // Library card cover box in DIPs (card width × cover row height in MainWindow.xaml).
    private const double CardCoverWidth = 230;
    private const double CardCoverHeight = 300;
    private CancellationTokenSource? _coverLoads;

    /// <summary>
    /// Fills card covers without blocking the UI: cached covers are set at once,
    /// the rest are decoded on worker threads. A newer refresh (page change, search
    /// keystroke) cancels loads for cards that are no longer shown.
    /// </summary>
    private void StartCoverLoads(IReadOnlyList<CoverCardViewModel> cards)
    {
        _coverLoads?.Cancel();
        _coverLoads?.Dispose();
        _coverLoads = new CancellationTokenSource();
        var token = _coverLoads.Token;

        foreach (var card in cards)
        {
            var path = card.CoverPath;
            if (string.IsNullOrWhiteSpace(path)) continue;

            var cached = CoverImageLoader.TryGetCached(path, CardCoverWidth, CardCoverHeight);
            if (cached is not null)
            {
                card.CoverImage = cached;
                continue;
            }

            _ = LoadCoverAsync(card, path, token);
        }
    }

    private async Task LoadCoverAsync(CoverCardViewModel card, string path, CancellationToken token)
    {
        var image = await CoverImageLoader.LoadAsync(path, CardCoverWidth, CardCoverHeight, token);
        // The await resumes on the UI thread; skip cards replaced by a newer refresh.
        if (image is not null && !token.IsCancellationRequested)
            card.CoverImage = image;
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
        // This runs on every search keystroke; only touch the disk when something changed.
        _library.SaveDevelopersIfDirty();

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

        var pageCards = cards.Skip((_currentPage - 1) * pageSize).Take(pageSize).ToList();
        DevelopersItems.ItemsSource = pageCards;
        StartCoverLoads(pageCards);
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
        AddExecutables(dialog.FileNames, openEditorIfSingle: true);
        RefreshLibrary();
    }

    /// <summary>
    /// Adds the given executables in one library save. When exactly one new game was
    /// requested and added, opens its editor (the old single-file behaviour).
    /// </summary>
    private void AddExecutables(IReadOnlyCollection<string> exes, bool openEditorIfSingle)
    {
        var entries = exes
            .Where(exe => File.Exists(exe) && exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            .Select(exe => new GameEntry
            {
                Name = ScannerService.InferNameFromExe(exe),
                ExePath = Path.GetFullPath(exe)
            })
            .ToList();

        var added = _library.AddRange(entries);
        if (openEditorIfSingle && exes.Count == 1 && added.Count == 1)
        {
            var editor = new GameEditorWindow(_library, added[0]) { Owner = this };
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
        // The scan runs on a worker thread inside this modal dialog, so the window
        // keeps painting and long scans can be cancelled.
        var progress = new ScanProgressWindow(folder, _library.BuildExeIndex()) { Owner = this };
        progress.ShowDialog();

        if (progress.Error is not null)
        {
            MessageBox.Show(this,
                LocalizationService.Bi("No se pudo escanear la carpeta.\n\n", "The folder could not be scanned.\n\n") + progress.Error.Message,
                LocalizationService.Bi("Escaneo", "Scan"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var candidates = progress.Candidates;
        if (candidates is null) return; // cancelled by the user

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
        // One save for the whole import instead of one per game.
        _library.AddRange(scan.SelectedCandidates.Select(c => new GameEntry { Name = c.Name, ExePath = c.ExePath }));
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
        var dialog = new ShortcutWindow(game, _library) { Owner = this };
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
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;
        e.Handled = true;

        // Finish the drag-and-drop first: work done inside Drop (dialogs, scans)
        // keeps Explorer's drag operation frozen until it returns.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var exes = paths.Where(p => File.Exists(p) && p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)).ToList();
            if (exes.Count > 0) AddExecutables(exes, openEditorIfSingle: false);
            foreach (var folder in paths.Where(Directory.Exists))
                ScanAndImport(folder);
            RefreshLibrary();
        }), System.Windows.Threading.DispatcherPriority.Background);
    }
}
