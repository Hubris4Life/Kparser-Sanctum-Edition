using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KParser.Sanctum.UI.Controls;
using KParser.Sanctum.UI.Models;

namespace TimelineRenderSmoke;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        RenderTheme("dark", "#CAA656", "#3B424C", "#EBEDF0", "#AAB0B8", "#22262C");
        RenderTheme("light", "#9B7423", "#D4D0C7", "#1F2226", "#626870", "#FCFBF8");
        Console.WriteLine("timeline-dark-theme=rendered");
        Console.WriteLine("timeline-light-theme=rendered");
        Console.WriteLine("timeline-dynamic-resources=verified");
    }

    private static void RenderTheme(
        string theme,
        string accent,
        string rule,
        string text,
        string muted,
        string canvas)
    {
        var resources = (ResourceDictionary)XamlReader.Parse($$"""
            <ResourceDictionary
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Color x:Key="AccentColor">{{accent}}</Color>
                <Color x:Key="RuleColor">{{rule}}</Color>
                <Color x:Key="CanvasTextColor">{{text}}</Color>
                <Color x:Key="CanvasMutedColor">{{muted}}</Color>
                <Color x:Key="CanvasColor">{{canvas}}</Color>
                <SolidColorBrush x:Key="AccentBrush" Color="{DynamicResource AccentColor}" />
                <SolidColorBrush x:Key="RuleBrush" Color="{DynamicResource RuleColor}" />
                <SolidColorBrush x:Key="CanvasTextBrush" Color="{DynamicResource CanvasTextColor}" />
                <SolidColorBrush x:Key="CanvasMutedBrush" Color="{DynamicResource CanvasMutedColor}" />
                <SolidColorBrush x:Key="CanvasBrush" Color="{DynamicResource CanvasColor}" />
            </ResourceDictionary>
            """);

        var rows = new ObservableCollection<CombatantRow>
        {
            CreateRow(1, "00:10", 1250, 125.0),
            CreateRow(2, "00:20", 2800, 280.0),
            CreateRow(3, "00:30", 900, 90.0),
            CreateRow(4, "00:40", 4100, 410.0)
        };
        var timeline = new DamageTimelineControl
        {
            ItemsSource = rows,
            Width = 900,
            Height = 380
        };
        var host = new Grid
        {
            Width = 900,
            Height = 380,
            Resources = resources,
            Background = (Brush)resources["CanvasBrush"]
        };
        host.Children.Add(timeline);
        host.Measure(new Size(900, 380));
        host.Arrange(new Rect(0, 0, 900, 380));
        host.UpdateLayout();

        var bitmap = new RenderTargetBitmap(900, 380, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(host);
        if (!timeline.RenderAttempted)
            throw new InvalidOperationException($"The {theme} timeline did not reach its WPF renderer.");
        if (timeline.LastRenderFailure is not null)
        {
            throw new InvalidOperationException(
                $"The {theme} timeline fell back after a render failure.",
                timeline.LastRenderFailure);
        }
        var pixels = new byte[900 * 380 * 4];
        bitmap.CopyPixels(pixels, 900 * 4, 0);
        if (!pixels.Where((_, index) => index % 4 == 3).Any(alpha => alpha != 0))
            throw new InvalidOperationException($"The {theme} timeline rendered a fully transparent image.");
    }

    private static CombatantRow CreateRow(int rank, string name, long damage, double dps) => new()
    {
        Rank = rank,
        Name = name,
        Job = "10s interval",
        CombatantType = "Timeline",
        Damage = damage,
        Share = 0,
        Dps = dps,
        Melee = damage / 2,
        WeaponSkills = damage / 4,
        Magic = damage / 5,
        Other = damage / 20
    };
}
