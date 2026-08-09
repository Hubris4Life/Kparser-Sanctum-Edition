using System.Windows;
using System.Windows.Input;

namespace KParser.Sanctum.UI;

public partial class PlayerSnapshotNameWindow : Window
{
    public PlayerSnapshotNameWindow(string suggestedLabel)
    {
        InitializeComponent();
        LabelTextBox.Text = suggestedLabel;
        Loaded += (_, _) =>
        {
            Topmost = true;
            Activate();
            LabelTextBox.Focus();
            LabelTextBox.SelectAll();
        };
    }

    public string SnapshotLabel => LabelTextBox.Text.Trim();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SnapshotLabel))
        {
            MessageBox.Show(this, "Enter a name for this player parse.", "Save Player Parse", MessageBoxButton.OK, MessageBoxImage.Information);
            LabelTextBox.Focus();
            return;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            return;
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void LabelTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            Save_Click(sender, e);
        else if (e.Key == Key.Escape)
            Cancel_Click(sender, e);
    }
}
