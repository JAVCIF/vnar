using System.Windows;
using LocaleGameHub.Models;
using LocaleGameHub.Services;

namespace LocaleGameHub;

public partial class ScanWindow : Window
{
    private readonly List<ScanCandidate> _candidates;
    public IReadOnlyList<ScanCandidate> SelectedCandidates => _candidates.Where(c => c.Selected).ToList();

    public ScanWindow(List<ScanCandidate> candidates)
    {
        InitializeComponent();
        DarkTitleBarService.Apply(this);
        LocalizationService.Apply(this);
        _candidates = candidates;
        CandidatesGrid.ItemsSource = _candidates;
        if (CandidatesGrid.Columns.Count >= 4)
        {
            CandidatesGrid.Columns[0].Header = LocalizationService.Bi("Añadir", "Add");
            CandidatesGrid.Columns[1].Header = LocalizationService.Bi("Nombre", "Name");
            CandidatesGrid.Columns[2].Header = LocalizationService.Bi("Ejecutable", "Executable");
            CandidatesGrid.Columns[3].Header = LocalizationService.Bi("Tamaño", "Size");
        }
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var c in _candidates) c.Selected = true;
        CandidatesGrid.Items.Refresh();
    }

    private void SelectNone_Click(object sender, RoutedEventArgs e)
    {
        foreach (var c in _candidates) c.Selected = false;
        CandidatesGrid.Items.Refresh();
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        CandidatesGrid.CommitEdit();
        CandidatesGrid.CommitEdit();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
