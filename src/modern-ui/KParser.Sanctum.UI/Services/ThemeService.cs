using System.Windows;
using System.Windows.Media;

namespace KParser.Sanctum.UI.Services;

internal static class ThemeService
{
    private static readonly IReadOnlyDictionary<string, Color> DarkColors = CreatePalette(
        chrome: "#1B1E23",
        panel: "#22262C",
        raised: "#2C3139",
        field: "#16191D",
        border: "#424953",
        accent: "#CAA656",
        accentHover: "#E0BE6F",
        primaryText: "#EBEDF0",
        mutedText: "#AAB0B8",
        canvas: "#22262C",
        canvasText: "#EBEDF0",
        canvasMuted: "#AAB0B8",
        rule: "#3B424C",
        selectedRow: "#3B3424",
        selectedRowText: "#F0D18A",
        rowHover: "#2C3139",
        footer: "#1F242A",
        detailText: "#AAB0B8",
        accentText: "#CAA656");

    private static readonly IReadOnlyDictionary<string, Color> LightColors = CreatePalette(
        chrome: "#ECE8DE",
        panel: "#F5F2EB",
        raised: "#E3DED3",
        field: "#FFFFFF",
        border: "#B7B0A4",
        accent: "#9B7423",
        accentHover: "#7D5B18",
        primaryText: "#20242A",
        mutedText: "#626870",
        canvas: "#FCFBF8",
        canvasText: "#1F2226",
        canvasMuted: "#626870",
        rule: "#D4D0C7",
        selectedRow: "#F4E9C9",
        selectedRowText: "#493813",
        rowHover: "#F0ECE4",
        footer: "#EEECE6",
        detailText: "#46505B",
        accentText: "#765A1D");

    public static void Apply(FrameworkElement element, bool lightMode)
    {
        var palette = lightMode ? LightColors : DarkColors;
        foreach (var pair in palette)
        {
            if (element.Resources.Contains(pair.Key))
                element.Resources[pair.Key] = pair.Value;
        }
    }

    private static IReadOnlyDictionary<string, Color> CreatePalette(
        string chrome,
        string panel,
        string raised,
        string field,
        string border,
        string accent,
        string accentHover,
        string primaryText,
        string mutedText,
        string canvas,
        string canvasText,
        string canvasMuted,
        string rule,
        string selectedRow,
        string selectedRowText,
        string rowHover,
        string footer,
        string detailText,
        string accentText) => new Dictionary<string, Color>
    {
        ["ChromeColor"] = Parse(chrome),
        ["PanelColor"] = Parse(panel),
        ["RaisedColor"] = Parse(raised),
        ["FieldColor"] = Parse(field),
        ["BorderColor"] = Parse(border),
        ["AccentColor"] = Parse(accent),
        ["AccentHoverColor"] = Parse(accentHover),
        ["PrimaryTextColor"] = Parse(primaryText),
        ["MutedTextColor"] = Parse(mutedText),
        ["CanvasColor"] = Parse(canvas),
        ["CanvasTextColor"] = Parse(canvasText),
        ["CanvasMutedColor"] = Parse(canvasMuted),
        ["RuleColor"] = Parse(rule),
        ["SelectedRowColor"] = Parse(selectedRow),
        ["SelectedRowTextColor"] = Parse(selectedRowText),
        ["RowHoverColor"] = Parse(rowHover),
        ["FooterColor"] = Parse(footer),
        ["DetailTextColor"] = Parse(detailText),
        ["AccentTextColor"] = Parse(accentText)
    };

    private static Color Parse(string value) =>
        (Color)ColorConverter.ConvertFromString(value);
}
