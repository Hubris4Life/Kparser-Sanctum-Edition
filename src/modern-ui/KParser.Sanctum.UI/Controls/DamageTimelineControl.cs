using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using KParser.Sanctum.UI.Models;
using KParser.Sanctum.UI.Services;

namespace KParser.Sanctum.UI.Controls;

internal sealed class DamageTimelineControl : FrameworkElement
{
    private static readonly object DiagnosticLogGate = new();
    private static DateTime nextDiagnosticLogUtc = DateTime.MinValue;

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource),
        typeof(IEnumerable<CombatantRow>),
        typeof(DamageTimelineControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, ItemsSourceChanged));

    private readonly List<CombatantRow> subscribedRows = [];
    private INotifyCollectionChanged? subscribedCollection;
    private CombatantRow[] renderedRows = [];
    private Rect renderedPlot;

    internal bool RenderAttempted { get; private set; }
    internal Exception? LastRenderFailure { get; private set; }

    public DamageTimelineControl()
    {
        Cursor = Cursors.Cross;
        SnapsToDevicePixels = true;
    }

    public IEnumerable<CombatantRow>? ItemsSource
    {
        get => (IEnumerable<CombatantRow>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        RenderAttempted = true;
        LastRenderFailure = null;
        renderedPlot = Rect.Empty;
        try
        {
            renderedRows = SnapshotRows();

            var textBrush = GetBrush("CanvasTextBrush", Color.FromRgb(235, 237, 240));
            var mutedBrush = GetBrush("CanvasMutedBrush", Color.FromRgb(170, 176, 184));
            var ruleBrush = GetBrush("RuleBrush", Color.FromRgb(59, 66, 76));
            var intervalBrush = GetBrush("AccentBrush", Color.FromRgb(202, 166, 86));
            var cumulativeBrush = new SolidColorBrush(Color.FromRgb(68, 190, 181));
            FreezeIfPossible(cumulativeBrush);

            if (renderedRows.Length == 0)
            {
                DrawText(
                    drawingContext,
                    "Waiting for observed damage to build the timeline",
                    14,
                    textBrush,
                    new Point(Math.Max(20, ActualWidth / 2 - 175), Math.Max(20, ActualHeight / 2 - 10)));
                return;
            }

            if (!double.IsFinite(ActualWidth) || !double.IsFinite(ActualHeight) ||
                ActualWidth <= 0 || ActualHeight <= 0)
            {
                return;
            }

        const double left = 70;
        const double right = 82;
        const double top = 48;
        const double bottom = 46;
        renderedPlot = new Rect(
            left,
            top,
            Math.Max(1, ActualWidth - left - right),
            Math.Max(1, ActualHeight - top - bottom));
        if (renderedPlot.Width < 80 || renderedPlot.Height < 60)
            return;

        DrawLegend(drawingContext, intervalBrush, cumulativeBrush, textBrush);

        long maximumInterval = Math.Max(1, renderedRows.Max(row => Math.Max(0, row.Damage)));
        long maximumCumulative = Math.Max(1, SumDamage(renderedRows));
        var gridPen = new Pen(ruleBrush, 1);
        FreezeIfPossible(gridPen);
        for (var index = 0; index <= 4; index++)
        {
            var fraction = index / 4.0;
            var y = renderedPlot.Bottom - renderedPlot.Height * fraction;
            drawingContext.DrawLine(gridPen, new Point(renderedPlot.Left, y), new Point(renderedPlot.Right, y));
            DrawText(
                drawingContext,
                FormatCompactNumber((long)Math.Round(maximumInterval * fraction)),
                10,
                mutedBrush,
                new Point(4, y - 7));
            DrawRightAlignedText(
                drawingContext,
                FormatCompactNumber((long)Math.Round(maximumCumulative * fraction)),
                10,
                mutedBrush,
                new Point(ActualWidth - 4, y - 7));
        }

        var barBrush = intervalBrush.Clone();
        barBrush.Opacity = 0.46;
        FreezeIfPossible(barBrush);
        var barSlot = renderedPlot.Width / renderedRows.Length;
        var barWidth = Math.Max(1, Math.Min(18, barSlot * 0.72));
        long cumulative = 0;
        var cumulativePoints = new List<Point>(renderedRows.Length);
        for (var index = 0; index < renderedRows.Length; index++)
        {
            var row = renderedRows[index];
            var damage = Math.Max(0, row.Damage);
            var x = renderedPlot.Left + (index + 0.5) * barSlot;
            var barHeight = renderedPlot.Height * damage / maximumInterval;
            drawingContext.DrawRoundedRectangle(
                barBrush,
                null,
                new Rect(x - barWidth / 2, renderedPlot.Bottom - barHeight, barWidth, barHeight),
                1.5,
                1.5);
            cumulative = SaturatingAdd(cumulative, damage);
            cumulativePoints.Add(new Point(
                x,
                renderedPlot.Bottom - renderedPlot.Height * cumulative / maximumCumulative));
        }

        if (cumulativePoints.Count == 1)
        {
            drawingContext.DrawEllipse(cumulativeBrush, null, cumulativePoints[0], 3, 3);
        }
        else
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(cumulativePoints[0], false, false);
                context.PolyLineTo(cumulativePoints.Skip(1).ToArray(), true, false);
            }
            FreezeIfPossible(geometry);
            var cumulativePen = new Pen(cumulativeBrush, 2.4);
            FreezeIfPossible(cumulativePen);
            drawingContext.DrawGeometry(null, cumulativePen, geometry);
        }

        var labelStep = Math.Max(1, (int)Math.Ceiling(renderedRows.Length / 7.0));
        for (var index = 0; index < renderedRows.Length; index += labelStep)
        {
            var x = renderedPlot.Left + (index + 0.5) * barSlot;
            DrawCenteredText(drawingContext, renderedRows[index].Name, 10, mutedBrush, new Point(x, renderedPlot.Bottom + 11));
        }
        if ((renderedRows.Length - 1) % labelStep != 0)
        {
            DrawRightAlignedText(
                drawingContext,
                renderedRows[^1].Name,
                10,
                mutedBrush,
                new Point(renderedPlot.Right, renderedPlot.Bottom + 11));
        }
        }
        catch (Exception ex)
        {
            LastRenderFailure = ex;
            renderedRows = [];
            renderedPlot = Rect.Empty;
            ToolTip = null;
            LogRenderFailure(ex);
            try
            {
                DrawText(
                    drawingContext,
                    "Timeline temporarily unavailable",
                    13,
                    GetBrush("CanvasMutedBrush", Color.FromRgb(170, 176, 184)),
                    new Point(18, 18));
            }
            catch
            {
                // Rendering failures must never terminate the parser UI.
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (renderedRows.Length == 0 || renderedPlot.IsEmpty || renderedPlot.Width <= 0 ||
            !renderedPlot.Contains(e.GetPosition(this)))
        {
            ToolTip = null;
            return;
        }

        var point = e.GetPosition(this);
        var index = Math.Clamp(
            (int)((point.X - renderedPlot.Left) / renderedPlot.Width * renderedRows.Length),
            0,
            renderedRows.Length - 1);
        var row = renderedRows[index];
        var cumulative = SumDamage(renderedRows.Take(index + 1));
        ToolTip = $"{row.Name}  |  {row.Damage:N0} damage  |  {row.Dps:N1} DPS\n" +
                  $"Melee {row.Melee:N0}  ·  WS {row.WeaponSkills:N0}  ·  Magic {row.Magic:N0}  ·  Other {row.Other:N0}\n" +
                  $"Cumulative {cumulative:N0}";
    }

    private static void ItemsSourceChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        var control = (DamageTimelineControl)target;
        control.Unsubscribe();
        control.Subscribe(args.NewValue as IEnumerable<CombatantRow>);
        control.InvalidateVisual();
    }

    private void Subscribe(IEnumerable<CombatantRow>? rows)
    {
        subscribedCollection = rows as INotifyCollectionChanged;
        if (subscribedCollection is not null)
            subscribedCollection.CollectionChanged += CollectionChanged;
        SubscribeRows(rows);
    }

    private void SubscribeRows(IEnumerable<CombatantRow>? rows)
    {
        foreach (var row in subscribedRows)
            row.PropertyChanged -= RowPropertyChanged;
        subscribedRows.Clear();
        if (rows is null)
            return;
        foreach (var row in rows)
        {
            row.PropertyChanged += RowPropertyChanged;
            subscribedRows.Add(row);
        }
    }

    private void Unsubscribe()
    {
        if (subscribedCollection is not null)
            subscribedCollection.CollectionChanged -= CollectionChanged;
        subscribedCollection = null;
        SubscribeRows(null);
    }

    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshSubscriptionsAndVisual();
    }

    private void RowPropertyChanged(object? sender, PropertyChangedEventArgs e) => InvalidateOnDispatcher();

    private CombatantRow[] SnapshotRows()
    {
        var source = ItemsSource;
        if (source is null)
            return [];

        try
        {
            return source
                .Where(row => row is not null)
                .OrderBy(row => row.Rank)
                .ToArray();
        }
        catch (InvalidOperationException)
        {
            // An ObservableCollection can change between layout and render.
            // Retaining the previous immutable snapshot is safe for one frame.
            return renderedRows;
        }
    }

    private void RefreshSubscriptionsAndVisual()
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(new Action(RefreshSubscriptionsAndVisual));
            return;
        }

        SubscribeRows(ItemsSource);
        InvalidateVisual();
    }

    private void InvalidateOnDispatcher()
    {
        if (Dispatcher.CheckAccess())
        {
            InvalidateVisual();
            return;
        }

        _ = Dispatcher.BeginInvoke(new Action(InvalidateVisual));
    }

    private void DrawLegend(
        DrawingContext drawingContext,
        Brush intervalBrush,
        Brush cumulativeBrush,
        Brush textBrush)
    {
        drawingContext.DrawRectangle(intervalBrush, null, new Rect(renderedPlot.Left, 17, 14, 10));
        DrawText(drawingContext, "Interval damage", 11, textBrush, new Point(renderedPlot.Left + 21, 13));
        var start = renderedPlot.Left + 137;
        var cumulativePen = new Pen(cumulativeBrush, 2.4);
        FreezeIfPossible(cumulativePen);
        drawingContext.DrawLine(cumulativePen, new Point(start, 22), new Point(start + 18, 22));
        DrawText(drawingContext, "Cumulative damage", 11, textBrush, new Point(start + 25, 13));
    }

    private static void FreezeIfPossible(Freezable value)
    {
        // Theme brushes contain DynamicResource expressions. A Pen or cloned Brush
        // that references one cannot be frozen, and calling Freeze() throws during
        // WPF layout. Freezing is only a rendering optimization, so leave those
        // objects mutable and owned by the UI thread.
        if (value.CanFreeze)
            value.Freeze();
    }

    private Brush GetBrush(string resourceKey, Color fallback) =>
        TryFindResource(resourceKey) as Brush ?? new SolidColorBrush(fallback);

    private void DrawText(DrawingContext context, string text, double size, Brush brush, Point point) =>
        context.DrawText(CreateText(text, size, brush), point);

    private void DrawCenteredText(DrawingContext context, string text, double size, Brush brush, Point point)
    {
        var formatted = CreateText(text, size, brush);
        context.DrawText(formatted, new Point(point.X - formatted.Width / 2, point.Y));
    }

    private void DrawRightAlignedText(DrawingContext context, string text, double size, Brush brush, Point point)
    {
        var formatted = CreateText(text, size, brush);
        context.DrawText(formatted, new Point(point.X - formatted.Width, point.Y));
    }

    private FormattedText CreateText(string text, double size, Brush brush) => new(
        text,
        CultureInfo.CurrentCulture,
        FlowDirection.LeftToRight,
        new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
        size,
        brush,
        VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private static string FormatCompactNumber(long amount)
    {
        if (amount >= 1_000_000)
            return (amount / 1_000_000.0).ToString("0.#", CultureInfo.InvariantCulture) + "m";
        if (amount >= 1_000)
            return (amount / 1_000.0).ToString("0.#", CultureInfo.InvariantCulture) + "k";
        return amount.ToString("N0", CultureInfo.InvariantCulture);
    }

    private static long SumDamage(IEnumerable<CombatantRow> rows)
    {
        long total = 0;
        foreach (var row in rows)
            total = SaturatingAdd(total, Math.Max(0, row.Damage));
        return total;
    }

    private static long SaturatingAdd(long left, long right) =>
        right > long.MaxValue - left ? long.MaxValue : left + right;

    private static void LogRenderFailure(Exception exception)
    {
        lock (DiagnosticLogGate)
        {
            var now = DateTime.UtcNow;
            if (now < nextDiagnosticLogUtc)
                return;
            nextDiagnosticLogUtc = now.AddMinutes(1);

            Trace.TraceError("Damage timeline rendering failed: {0}", exception);
            ApplicationDiagnostics.LogHandledException("Damage timeline rendering", exception);
            try
            {
                var directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "KParser Sanctum Modern",
                    "Logs");
                Directory.CreateDirectory(directory);
                File.AppendAllText(
                    Path.Combine(directory, "timeline-errors.log"),
                    $"[{DateTime.Now:O}] {exception}{Environment.NewLine}{Environment.NewLine}");
            }
            catch
            {
                // Diagnostics must never turn a recoverable graph error into an app failure.
            }
        }
    }
}
