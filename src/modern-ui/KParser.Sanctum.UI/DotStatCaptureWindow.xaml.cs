using System.Windows;
using System.Windows.Input;

namespace KParser.Sanctum.UI;

public partial class DotStatCaptureWindow : Window
{
    public DotStatCaptureWindow(IEnumerable<string> knownCharacters, string? suggestedCharacter)
    {
        InitializeComponent();

        var characters = knownCharacters
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        CharacterNameBox.ItemsSource = characters;
        CharacterNameBox.Text = suggestedCharacter?.Trim() ?? string.Empty;

        Loaded += (_, _) =>
        {
            Topmost = true;
            Activate();
            CharacterNameBox.Focus();
            if (CharacterNameBox.Template.FindName("PART_EditableTextBox", CharacterNameBox) is System.Windows.Controls.TextBox editor)
                editor.SelectAll();
        };
    }

    public string CharacterName => CharacterNameBox.Text.Trim();

    private void Capture_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(CharacterName))
        {
            MessageBox.Show(
                this,
                "Enter the name of the character currently logged into FFXI.",
                "Register Player Stats",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            CharacterNameBox.Focus();
            return;
        }

        if (CharacterName.Length > 32)
        {
            MessageBox.Show(
                this,
                "The character name must be 32 characters or fewer.",
                "Register Player Stats",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            CharacterNameBox.Focus();
            return;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            return;
        try { DragMove(); } catch (InvalidOperationException) { }
    }

    private void CharacterNameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            Capture_Click(sender, e);
        else if (e.Key == Key.Escape)
            Cancel_Click(sender, e);
    }
}
