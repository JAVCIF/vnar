using System.Text;
using System.Windows;
using System.Windows.Threading;
using LocaleGameHub.Services;

namespace LocaleGameHub;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

        try
        {
            var launch = ParseLaunchRequest(e.Args, out var gameId);
            var isGameLaunch = launch != LaunchRequest.None;

            // Set the identity before creating any windows, including error dialogs.
            // Classic (.lnk) launches take the game's identity so pinned shortcuts keep
            // their own icon. Modern (vnar://) launches keep VNAR's identity, as Steam
            // and Playnite do, so Windows never associates a game with VNAR.exe.
            // Old shortcuts without an explicit AppID cannot claim the launcher UI.
            TaskbarIdentityService.SetCurrentProcessIdentity(launch == LaunchRequest.Classic ? gameId : null);

            if (launch == LaunchRequest.InvalidLink)
            {
                // A vnar:// link this version does not understand (e.g. from a web page):
                // never fall through to opening the library for it.
                MessageBox.Show(
                    LocalizationService.Bi("Ese enlace de VNAR no es válido.", "That VNAR link is not valid."),
                    "VNAR", MessageBoxButton.OK, MessageBoxImage.Warning);
                Shutdown(0);
                return;
            }

            // Only one library window per session; a second launch brings the
            // existing one to the front instead of loading a second copy of the data.
            if (!isGameLaunch && !SingleInstanceService.TryClaimPrimary())
            {
                Shutdown(0);
                return;
            }

            var preferences = new LibraryService();
            LocalizationService.SetLanguage(preferences.Settings.Language);

            if (isGameLaunch)
            {
                LaunchFromShortcut(preferences, gameId);
                Shutdown(0);
                return;
            }

            // If modern shortcuts were ever created, keep vnar:// pointing at this
            // VNAR.exe (a portable copy may have been moved since).
            try { ProtocolRegistrationService.RefreshIfRegistered(ShortcutService.ResolveVnarExecutable()); }
            catch { /* modern shortcuts keep the old path until the next start */ }

            var main = new MainWindow();
            MainWindow = main;
            main.Show();
            SingleInstanceService.ListenForActivation(() => Dispatcher.BeginInvoke(new Action(main.BringToFront)));
        }
        catch (Exception ex)
        {
            ShowStartupError(ex);
            Shutdown(-1);
        }
    }


    private enum LaunchRequest
    {
        None,
        Classic,     // --launch <id>  (from a .lnk)
        Modern,      // vnar://launch/<id>  (from a .url, via the protocol)
        InvalidLink  // vnar:// link that is not a valid launch command
    }

    private static LaunchRequest ParseLaunchRequest(string[] args, out Guid gameId)
    {
        gameId = Guid.Empty;
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (VnarUri.IsVnarUri(arg.Trim('"')))
                return VnarUri.TryParseLaunch(arg, out gameId) ? LaunchRequest.Modern : LaunchRequest.InvalidLink;

            if (arg.StartsWith("--launch=", StringComparison.OrdinalIgnoreCase))
                return Guid.TryParse(arg["--launch=".Length..].Trim('"'), out gameId) ? LaunchRequest.Classic : LaunchRequest.None;

            if (!arg.Equals("--launch", StringComparison.OrdinalIgnoreCase) || i + 1 >= args.Length) continue;
            return Guid.TryParse(args[i + 1].Trim('"'), out gameId) ? LaunchRequest.Classic : LaunchRequest.None;
        }
        return LaunchRequest.None;
    }

    private static void LaunchFromShortcut(LibraryService library, Guid gameId)
    {
        var game = library.Games.FirstOrDefault(g => g.Id == gameId);
        if (game is null)
        {
            var message = LocalizationService.Bi(
                "Ese acceso directo apunta a un juego que ya no existe en la biblioteca de VNAR.",
                "This shortcut points to a game that no longer exists in the VNAR library.");
            if (JsonStore.HasPendingRecoveries)
                message += "\n\n" + LocalizationService.Bi(
                    "Además, un archivo de datos de VNAR estaba dañado y se apartó. Abre VNAR para ver los detalles.",
                    "Also, a VNAR data file was damaged and was set aside. Open VNAR to see the details.");
            throw new InvalidOperationException(message);
        }

        var locale = new LocaleEmulatorService(library);
        locale.Launch(game);
        // Re-reads the library under the shared lock so edits made meanwhile in the
        // library window are not overwritten by this process's startup snapshot.
        library.RecordLaunch(game.Id, DateTime.UtcNow);
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrashLog("DispatcherUnhandledException", e.Exception);
        MessageBox.Show(
            BuildErrorMessage(e.Exception),
            LocalizationService.Bi("VNAR - Error inesperado", "VNAR - Unexpected error"),
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private void CurrentDomain_UnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            WriteCrashLog("UnhandledException", ex);
    }

    private static void ShowStartupError(Exception ex)
    {
        WriteCrashLog("Startup", ex);
        MessageBox.Show(
            BuildErrorMessage(ex),
            LocalizationService.Bi("VNAR no pudo iniciar", "VNAR could not start"),
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private static string BuildErrorMessage(Exception ex)
    {
        return LocalizationService.IsSpanish
            ? $"VNAR encontró un error al iniciar.\n\n{ex.GetType().Name}: {ex.Message}\n\nSe guardó un log en:\n{CrashLogPath}"
            : $"VNAR encountered a startup error.\n\n{ex.GetType().Name}: {ex.Message}\n\nA log was saved to:\n{CrashLogPath}";
    }

    private static string CrashLogPath
    {
        get
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VNAR");
            return Path.Combine(root, "crash.log");
        }
    }

    private static void WriteCrashLog(string stage, Exception ex)
    {
        try
        {
            var path = CrashLogPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var sb = new StringBuilder();
            sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {stage}");
            sb.AppendLine(ex.ToString());
            sb.AppendLine(new string('-', 80));
            File.AppendAllText(path, sb.ToString());
        }
        catch
        {
            // Never let logging hide the original error.
        }
    }
}
