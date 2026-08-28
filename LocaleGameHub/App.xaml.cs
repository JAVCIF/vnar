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
            var preferences = new LibraryService();
            LocalizationService.SetLanguage(preferences.Settings.Language);

            if (TryGetLaunchGameId(e.Args, out var gameId))
            {
                LaunchFromShortcut(preferences, gameId);
                Shutdown(0);
                return;
            }

            var main = new MainWindow();
            MainWindow = main;
            main.Show();
        }
        catch (Exception ex)
        {
            ShowStartupError(ex);
            Shutdown(-1);
        }
    }


    private static bool TryGetLaunchGameId(string[] args, out Guid gameId)
    {
        gameId = Guid.Empty;
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.StartsWith("--launch=", StringComparison.OrdinalIgnoreCase))
                return Guid.TryParse(arg["--launch=".Length..].Trim('"'), out gameId);

            if (!arg.Equals("--launch", StringComparison.OrdinalIgnoreCase) || i + 1 >= args.Length) continue;
            return Guid.TryParse(args[i + 1].Trim('"'), out gameId);
        }
        return false;
    }

    private static void LaunchFromShortcut(LibraryService library, Guid gameId)
    {
        var game = library.Games.FirstOrDefault(g => g.Id == gameId)
            ?? throw new InvalidOperationException(LocalizationService.Bi(
                "Ese acceso directo apunta a un juego que ya no existe en la biblioteca de VNAR.",
                "This shortcut points to a game that no longer exists in the VNAR library."));

        var locale = new LocaleEmulatorService(library);
        locale.Launch(game);
        game.LastPlayedUtc = DateTime.UtcNow;
        library.SaveGames();
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
