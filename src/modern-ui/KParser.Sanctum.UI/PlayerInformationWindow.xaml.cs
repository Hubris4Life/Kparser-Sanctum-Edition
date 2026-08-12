using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KParser.Sanctum.UI.Models;

namespace KParser.Sanctum.UI;

public partial class PlayerInformationWindow : Window
{
    internal PlayerInformationWindow(IEnumerable<PlayerInformationEntry> entries)
    {
        InitializeComponent();
        Entries = new ObservableCollection<PlayerInformationEntry>(entries.Select(item => item.Clone()));
        PlayersGrid.DataContext = Entries;
        EmptyStateText.Visibility = Entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Loaded += (_, _) =>
        {
            Activate();
            PlayersGrid.Focus();
        };
    }

    internal ObservableCollection<PlayerInformationEntry> Entries { get; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        PlayersGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        PlayersGrid.CommitEdit(DataGridEditingUnit.Row, true);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            return;
        try { DragMove(); } catch (InvalidOperationException) { }
    }
}
