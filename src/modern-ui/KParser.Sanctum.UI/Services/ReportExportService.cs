using System.Globalization;
using System.IO;
using System.Text;
using KParser.Sanctum.UI.Models;
using KParser.Sanctum.UI.ViewModels;

namespace KParser.Sanctum.UI.Services;

internal static class ReportExportService
{
    public static string BuildClipboardReport(MainWindowViewModel viewModel)
    {
        var builder = new StringBuilder();
        builder.AppendLine(viewModel.EncounterTitle);
        builder.AppendLine(viewModel.EncounterSubtitle);
        builder.AppendLine(string.Join(
            " | ",
            "Duration: " + viewModel.Duration,
            viewModel.SummaryTotalLabel + ": " + viewModel.TotalDamage,
            viewModel.SummaryRateLabel + ": " + viewModel.AllianceDps));
        builder.AppendLine();
        builder.AppendLine(string.Join('\t', GetHeaders(viewModel)));

        foreach (var row in viewModel.Combatants)
            builder.AppendLine(string.Join('\t', GetDisplayValues(row)));

        return builder.ToString().TrimEnd();
    }

    public static string BuildSelectedCombatant(MainWindowViewModel viewModel)
    {
        var row = viewModel.SelectedCombatant;
        if (row is null)
            return string.Empty;

        if (viewModel.IsChatSelected)
        {
            return string.Join(
                " | ",
                row.PrimaryText,
                row.Job,
                row.Name + ": " + row.Detail1Text);
        }

        var summary = string.Join(
            " | ",
            $"#{row.Rank} {row.Name} ({row.Job})",
            viewModel.PrimaryColumnLabel + ": " + row.DamageDisplay,
            viewModel.ShareColumnLabel + ": " + row.ShareDisplay,
            viewModel.RateColumnLabel + ": " + row.DpsDisplay,
            row.TopAction,
            row.Accuracy,
            row.CriticalRate);

        var lines = new List<string> { summary };
        if (viewModel.ShowDamageSourceFooter)
        {
            lines.Add(row.SourceBreakdownDisplay);
            lines.Add(row.PhysicalBreakdownDisplay);
        }
        return string.Join(
            Environment.NewLine,
            lines);
    }

    public static string BuildCurrentFightSummary(CurrentFightViewModel viewModel)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(
            " | ",
            "Live monitor: " + viewModel.EncounterName,
            "Time: " + viewModel.Duration,
            "Damage: " + viewModel.TotalDamage,
            "DPS: " + viewModel.AllianceDps));

        foreach (var row in viewModel.Combatants)
        {
            builder.AppendLine(string.Join(
                " | ",
                $"#{row.Rank} {row.Name} ({row.Job})",
                row.DamageDisplay + " damage",
                row.ShareDisplay,
                row.DpsDisplay + " DPS",
                row.AccuracyDisplay + " accuracy"));
        }

        return builder.ToString().TrimEnd();
    }

    public static string BuildCurrentFightCsv(CurrentFightViewModel viewModel)
    {
        var builder = new StringBuilder();
        AppendCsvRow(builder, "Report", "Live monitor - " + viewModel.EncounterName);
        AppendCsvRow(builder, "Duration", viewModel.Duration);
        AppendCsvRow(builder, "Total damage", viewModel.TotalDamage);
        AppendCsvRow(builder, "Alliance DPS", viewModel.AllianceDps);
        builder.AppendLine();
        AppendCsvRow(builder, "Rank", "Combatant", "Job", "Damage", "Share", "DPS", "Accuracy");
        foreach (var row in viewModel.Combatants)
        {
            AppendCsvRow(
                builder,
                row.Rank.ToString(CultureInfo.InvariantCulture),
                CleanText(row.Name),
                CleanText(row.Job),
                row.Damage.ToString(CultureInfo.InvariantCulture),
                row.Share.ToString(CultureInfo.InvariantCulture),
                row.Dps.ToString(CultureInfo.InvariantCulture),
                CleanText(row.AccuracyDisplay));
        }

        return builder.ToString();
    }

    public static string CreateCurrentFightFileName(CurrentFightViewModel viewModel)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safeTitle = new string(viewModel.EncounterName
            .Select(character => invalid.Contains(character) ? '-' : character)
            .ToArray());
        return $"KParser {safeTitle} {DateTime.Now:yyyy-MM-dd HHmm}.csv";
    }

    public static string BuildCsv(MainWindowViewModel viewModel)
    {
        var builder = new StringBuilder();
        AppendCsvRow(builder, "Report", viewModel.EncounterTitle);
        AppendCsvRow(builder, "Description", viewModel.EncounterSubtitle);
        AppendCsvRow(builder, "Duration", viewModel.Duration);
        AppendCsvRow(builder, viewModel.SummaryTotalLabel, viewModel.TotalDamage);
        AppendCsvRow(builder, viewModel.SummaryRateLabel, viewModel.AllianceDps);
        builder.AppendLine();
        AppendCsvRow(builder, GetHeaders(viewModel));

        foreach (var row in viewModel.Combatants)
            AppendCsvRow(builder, GetDisplayValues(row));

        return builder.ToString();
    }

    public static string CreateDefaultFileName(MainWindowViewModel viewModel)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var reportTitle = string.IsNullOrWhiteSpace(viewModel.EncounterTitle)
            ? "KParser report"
            : viewModel.EncounterTitle;
        var safeTitle = new string(reportTitle
            .Select(character => invalid.Contains(character) ? '-' : character)
            .ToArray());
        safeTitle = string.Join(" ", safeTitle.Split(
            [' ', '\t', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries));

        if (safeTitle.Length > 80)
            safeTitle = safeTitle[..80].Trim();

        return $"{safeTitle} {DateTime.Now:yyyy-MM-dd HHmm}.csv";
    }

    private static string[] GetHeaders(MainWindowViewModel viewModel) =>
    [
        "Rank",
        viewModel.NameColumnLabel,
        viewModel.SecondaryColumnLabel,
        viewModel.PrimaryColumnLabel,
        viewModel.ShareColumnLabel,
        viewModel.RateColumnLabel,
        viewModel.Detail1ColumnLabel,
        viewModel.Detail2ColumnLabel,
        viewModel.Detail3ColumnLabel,
        viewModel.Detail4ColumnLabel,
        "Top action",
        "Accuracy / detail",
        "Critical rate / detail"
    ];

    private static string[] GetDisplayValues(CombatantRow row) =>
    [
        row.Rank.ToString(CultureInfo.InvariantCulture),
        CleanText(row.Name),
        CleanText(row.Job),
        row.DamageDisplay,
        row.ShareDisplay,
        row.DpsDisplay,
        row.MeleeDisplay,
        row.WeaponSkillsDisplay,
        row.MagicDisplay,
        row.OtherDisplay,
        CleanText(row.TopAction),
        CleanText(row.Accuracy),
        CleanText(row.CriticalRate)
    ];

    private static string FormatRawAmount(long? value) =>
        value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;

    private static string CleanText(string value) =>
        value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    private static void AppendCsvRow(StringBuilder builder, params string[] values)
    {
        builder.AppendLine(string.Join(",", values.Select(EscapeCsv)));
    }

    private static string EscapeCsv(string value)
    {
        var safeValue = value ?? string.Empty;
        return safeValue.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? '"' + safeValue.Replace("\"", "\"\"") + '"'
            : safeValue;
    }
}
