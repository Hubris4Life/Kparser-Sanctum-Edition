using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace KParser.Sanctum.UI;

public partial class OverlayCustomizationWindow : Window
{
    private const string DefaultNameColor = "#F0D18A";
    private const string DefaultStatisticColor = "#EBEDF0";
    private bool previewReady;

    public OverlayCustomizationWindow(
        double textSize,
        bool boldText,
        string? nameColor,
        string? statisticColor)
    {
        InitializeComponent();
        previewReady = true;
        TextSizeSlider.Value = double.IsFinite(textSize)
            ? Math.Clamp(textSize, 9, 24)
            : 12;
        BoldTextCheckBox.IsChecked = boldText;
        NameColorBox.Text = NormalizeInitialColor(nameColor, DefaultNameColor);
        StatisticColorBox.Text = NormalizeInitialColor(statisticColor, DefaultStatisticColor);
        UpdatePreview();

        Loaded += (_, _) =>
        {
            Topmost = true;
            Activate();
        };
    }

    public double TextSize => Math.Round(TextSizeSlider.Value);
    public bool BoldText => BoldTextCheckBox.IsChecked == true;
    public string NameColor { get; private set; } = DefaultNameColor;
    public string StatisticColor { get; private set; } = DefaultStatisticColor;

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (!TryParseColor(NameColorBox.Text, out var nameColor, out var normalizedName))
        {
            ShowInvalidColor("Enter a valid player-name color, such as #F0D18A.", NameColorBox);
            return;
        }

        if (!TryParseColor(StatisticColorBox.Text, out var statisticColor, out var normalizedStatistic))
        {
            ShowInvalidColor("Enter a valid statistic color, such as #EBEDF0.", StatisticColorBox);
            return;
        }

        NameColor = normalizedName;
        StatisticColor = normalizedStatistic;
        NameColorBox.Text = normalizedName;
        StatisticColorBox.Text = normalizedStatistic;
        PreviewName.Foreground = new SolidColorBrush(nameColor);
        SetStatisticPreviewBrush(new SolidColorBrush(statisticColor));
        DialogResult = true;
    }

    private void RestoreDefaults_Click(object sender, RoutedEventArgs e)
    {
        TextSizeSlider.Value = 12;
        BoldTextCheckBox.IsChecked = true;
        NameColorBox.Text = DefaultNameColor;
        StatisticColorBox.Text = DefaultStatisticColor;
        UpdatePreview();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            return;
        try { DragMove(); } catch (InvalidOperationException) { }
    }

    private void Customization_ValueChanged(object sender, RoutedEventArgs e) => UpdatePreview();

    private void TextSizeSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e) => UpdatePreview();

    private void ColorBox_TextChanged(object sender, TextChangedEventArgs e) => UpdatePreview();

    private void UpdatePreview()
    {
        if (!previewReady)
            return;

        var fontWeight = BoldText ? FontWeights.Bold : FontWeights.Normal;
        foreach (var preview in GetPreviewText())
        {
            preview.FontSize = TextSize;
            preview.FontWeight = fontWeight;
        }
        TextSizeValue.Text = $"{TextSize:0}";

        UpdateColorPreview(NameColorBox, NameColorSwatch, PreviewName, DefaultNameColor);
        if (TryParseColor(StatisticColorBox.Text, out var statisticColor, out _))
        {
            StatisticColorBox.BorderBrush = (Brush)FindResource("BorderBrush");
            StatisticColorSwatch.Background = new SolidColorBrush(statisticColor);
            SetStatisticPreviewBrush(new SolidColorBrush(statisticColor));
        }
        else
        {
            StatisticColorBox.BorderBrush = Brushes.IndianRed;
            StatisticColorSwatch.Background = Brushes.Transparent;
            if (TryParseColor(DefaultStatisticColor, out var fallback, out _))
                SetStatisticPreviewBrush(new SolidColorBrush(fallback));
        }
    }

    private void UpdateColorPreview(
        TextBox textBox,
        Border swatch,
        TextBlock preview,
        string fallbackValue)
    {
        if (TryParseColor(textBox.Text, out var color, out _))
        {
            textBox.BorderBrush = (Brush)FindResource("BorderBrush");
            swatch.Background = new SolidColorBrush(color);
            preview.Foreground = new SolidColorBrush(color);
        }
        else
        {
            textBox.BorderBrush = Brushes.IndianRed;
            swatch.Background = Brushes.Transparent;
            if (TryParseColor(fallbackValue, out var fallback, out _))
                preview.Foreground = new SolidColorBrush(fallback);
        }
    }

    private void SetStatisticPreviewBrush(Brush brush)
    {
        PreviewDamage.Foreground = brush;
        PreviewShare.Foreground = brush;
        PreviewAccuracy.Foreground = brush;
        PreviewCritical.Foreground = brush;
    }

    private IEnumerable<TextBlock> GetPreviewText()
    {
        yield return PreviewName;
        yield return PreviewDamage;
        yield return PreviewShare;
        yield return PreviewAccuracy;
        yield return PreviewCritical;
    }

    private void ShowInvalidColor(string message, TextBox textBox)
    {
        MessageBox.Show(
            this,
            message,
            "Overlay Customizations",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        textBox.Focus();
        textBox.SelectAll();
    }

    private static string NormalizeInitialColor(string? value, string fallback) =>
        TryParseColor(value, out _, out var normalized) ? normalized : fallback;

    private static bool TryParseColor(string? value, out Color color, out string normalized)
    {
        try
        {
            color = (Color)ColorConverter.ConvertFromString(value?.Trim() ?? string.Empty);
            normalized = color.A == byte.MaxValue
                ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
                : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
            return true;
        }
        catch (Exception)
        {
            color = default;
            normalized = string.Empty;
            return false;
        }
    }
}
