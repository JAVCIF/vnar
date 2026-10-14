using System.Windows;
using System.Windows.Controls;
using LocaleGameHub.Models;
using LocaleGameHub.Services;

namespace LocaleGameHub;

/// <summary>
/// Modal progress dialog that runs the folder scan on a worker thread, so the
/// library window keeps painting and the user can cancel a long scan.
/// Built in code (no XAML) and styled with the app's shared resources.
/// </summary>
public sealed class ScanProgressWindow : Window
{
    private readonly string _folder;
    private readonly HashSet<string> _knownExes;
    private readonly CancellationTokenSource _cancel = new();
    private readonly TextBlock _status;
    private readonly Button _cancelButton;
    private bool _finished;

    /// <summary>New candidates (not already in the library); null if cancelled or failed.</summary>
    public List<ScanCandidate>? Candidates { get; private set; }

    public Exception? Error { get; private set; }

    public ScanProgressWindow(string folder, HashSet<string> knownExes)
    {
        _folder = folder;
        _knownExes = knownExes;

        Title = LocalizationService.Bi("Escaneando", "Scanning");
        Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Resources/VNAR.ico"));
        Width = 460;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "BgBrush");
        DarkTitleBarService.Apply(this);

        var title = new TextBlock
        {
            Text = LocalizationService.Bi("Buscando ejecutables en:", "Looking for executables in:"),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

        var path = new TextBlock
        {
            Text = folder,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = folder,
            Margin = new Thickness(0, 0, 0, 14)
        };
        path.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");

        var bar = new ProgressBar { IsIndeterminate = true, Height = 6, Margin = new Thickness(0, 0, 0, 10) };
        bar.SetResourceReference(ProgressBar.ForegroundProperty, "AccentBrush");
        bar.SetResourceReference(ProgressBar.BackgroundProperty, "PanelBrush");
        bar.BorderThickness = new Thickness(0);

        _status = new TextBlock { Margin = new Thickness(0, 0, 0, 16) };
        _status.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        UpdateStatus(new ScannerService.ScanProgress(0, 0));

        _cancelButton = new Button
        {
            Content = LocalizationService.Bi("Cancelar", "Cancel"),
            HorizontalAlignment = HorizontalAlignment.Right,
            MinWidth = 110
            // No IsCancel: WPF would set DialogResult and try to close while the scan
            // is still running. Esc is handled below and goes through RequestCancel.
        };
        _cancelButton.SetResourceReference(StyleProperty, "SecondaryButton");
        _cancelButton.Click += (_, _) => RequestCancel();

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(title);
        panel.Children.Add(path);
        panel.Children.Add(bar);
        panel.Children.Add(_status);
        panel.Children.Add(_cancelButton);
        Content = panel;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Escape) return;
            e.Handled = true;
            RequestCancel();
        };
        Loaded += async (_, _) => await RunAsync();
        Closing += (_, e) =>
        {
            // Closing with the title bar X while scanning behaves like Cancel.
            if (_finished) return;
            e.Cancel = true;
            RequestCancel();
        };
    }

    private void RequestCancel()
    {
        if (_finished) return;
        _cancelButton.IsEnabled = false;
        _status.Text = LocalizationService.Bi("Cancelando…", "Cancelling…");
        _cancel.Cancel();
    }

    private void UpdateStatus(ScannerService.ScanProgress p)
    {
        if (_cancel.IsCancellationRequested) return;
        _status.Text = LocalizationService.IsSpanish
            ? $"{p.FoldersVisited} carpetas revisadas · {p.ExecutablesFound} ejecutables encontrados"
            : $"{p.FoldersVisited} folders checked · {p.ExecutablesFound} executables found";
    }

    private async Task RunAsync()
    {
        // Progress<T> captures the UI context here, so reports update the label safely.
        var progress = new Progress<ScannerService.ScanProgress>(UpdateStatus);
        var token = _cancel.Token;
        try
        {
            Candidates = await Task.Run(() =>
                ScannerService.Scan(_folder, 400, progress, token)
                    .Where(c => !_knownExes.Contains(LibraryService.NormalizeExePath(c.ExePath)))
                    .ToList(), token);
        }
        catch (OperationCanceledException)
        {
            Candidates = null;
        }
        catch (Exception ex)
        {
            Candidates = null;
            Error = ex;
        }
        finally
        {
            _finished = true;
            _cancel.Dispose();
            DialogResult = Candidates is not null;
        }
    }
}
