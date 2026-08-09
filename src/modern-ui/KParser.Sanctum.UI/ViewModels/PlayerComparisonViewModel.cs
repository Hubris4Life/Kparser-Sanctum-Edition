using System.Collections.ObjectModel;
using KParser.Sanctum.UI.Infrastructure;
using KParser.Sanctum.UI.Models;
using KParser.Sanctum.UI.Services;

namespace KParser.Sanctum.UI.ViewModels;

internal sealed class PlayerComparisonViewModel : ObservableObject
{
    private readonly PlayerParseService service;
    private PlayerParseSnapshot? selectedFirst;
    private PlayerParseSnapshot? selectedSecond;
    private string emptyStateText = "Save at least two player parses to compare builds.";

    public PlayerComparisonViewModel(PlayerParseService service)
    {
        this.service = service;
        Snapshots = [];
        Metrics = [];
        Refresh();
    }

    public ObservableCollection<PlayerParseSnapshot> Snapshots { get; }
    public ObservableCollection<PlayerComparisonMetric> Metrics { get; }

    public PlayerParseSnapshot? SelectedFirst
    {
        get => selectedFirst;
        set
        {
            if (SetProperty(ref selectedFirst, value))
                RebuildMetrics();
        }
    }

    public PlayerParseSnapshot? SelectedSecond
    {
        get => selectedSecond;
        set
        {
            if (SetProperty(ref selectedSecond, value))
                RebuildMetrics();
        }
    }

    public string EmptyStateText
    {
        get => emptyStateText;
        private set => SetProperty(ref emptyStateText, value);
    }

    public bool HasComparison => SelectedFirst is not null && SelectedSecond is not null;

    public void Refresh(string? preferPlayer = null)
    {
        var firstId = SelectedFirst?.Id;
        var secondId = SelectedSecond?.Id;
        var loaded = service.LoadAll();

        Snapshots.Clear();
        foreach (var snapshot in loaded)
            Snapshots.Add(snapshot);

        SelectedFirst = Snapshots.FirstOrDefault(snapshot => snapshot.Id == firstId);
        SelectedSecond = Snapshots.FirstOrDefault(snapshot => snapshot.Id == secondId);

        var candidates = string.IsNullOrWhiteSpace(preferPlayer)
            ? Snapshots.ToArray()
            : Snapshots.Where(snapshot => string.Equals(
                snapshot.PlayerName,
                preferPlayer,
                StringComparison.OrdinalIgnoreCase)).ToArray();
        if (candidates.Length < 2)
            candidates = Snapshots.ToArray();

        if (SelectedSecond is null)
            SelectedSecond = candidates.FirstOrDefault();
        if (SelectedFirst is null)
            SelectedFirst = candidates.Skip(1).FirstOrDefault() ?? candidates.FirstOrDefault();

        RebuildMetrics();
    }

    public void Swap()
    {
        var first = SelectedFirst;
        SelectedFirst = SelectedSecond;
        SelectedSecond = first;
    }

    public void Delete(PlayerParseSnapshot snapshot)
    {
        service.Delete(snapshot);
        Refresh();
    }

    private void RebuildMetrics()
    {
        Metrics.Clear();
        RaisePropertyChanged(nameof(HasComparison));

        if (SelectedFirst is null || SelectedSecond is null)
        {
            EmptyStateText = Snapshots.Count < 2
                ? "Save at least two player parses to compare builds."
                : "Choose a parse on both sides to compare them.";
            return;
        }

        EmptyStateText = string.Empty;
        AddNumber("Total damage", SelectedFirst.TotalDamage, SelectedSecond.TotalDamage);
        AddDecimal("DPS", SelectedFirst.Dps, SelectedSecond.Dps);
        AddDuration("Duration", SelectedFirst.DurationSeconds, SelectedSecond.DurationSeconds);
        AddNumber("Melee damage", SelectedFirst.MeleeDamage, SelectedSecond.MeleeDamage);
        AddNumber("Ranged damage", SelectedFirst.RangedDamage, SelectedSecond.RangedDamage);
        AddNumber("Weapon skill damage", SelectedFirst.WeaponSkillDamage, SelectedSecond.WeaponSkillDamage);
        AddNumber("Ability damage", SelectedFirst.AbilityDamage, SelectedSecond.AbilityDamage);
        AddNumber("Magic damage", SelectedFirst.MagicDamage, SelectedSecond.MagicDamage);
        AddNumber("Skillchain damage", SelectedFirst.SkillchainDamage, SelectedSecond.SkillchainDamage);
        AddNumber("Additional-effect damage", SelectedFirst.AdditionalEffectDamage, SelectedSecond.AdditionalEffectDamage);
        AddNumber("Reactive damage", SelectedFirst.ReactiveDamage, SelectedSecond.ReactiveDamage);
        AddPercent("Physical accuracy", SelectedFirst.AccuracyPercent, SelectedSecond.AccuracyPercent);
        AddPercent("Critical hit rate", SelectedFirst.CriticalRatePercent, SelectedSecond.CriticalRatePercent);
        AddNumber("Physical attempts", SelectedFirst.PhysicalAttempts, SelectedSecond.PhysicalAttempts);
        AddNumber("Physical hits", SelectedFirst.PhysicalHits, SelectedSecond.PhysicalHits);
        AddNumber("Physical misses", SelectedFirst.PhysicalMisses, SelectedSecond.PhysicalMisses);
        AddNumber("Critical hits", SelectedFirst.CriticalHits, SelectedSecond.CriticalHits);
    }

    private void AddNumber(string metric, long first, long second)
    {
        Metrics.Add(new PlayerComparisonMetric
        {
            Metric = metric,
            FirstValue = first.ToString("N0"),
            SecondValue = second.ToString("N0"),
            Change = FormatDelta(first, second, "N0")
        });
    }

    private void AddDecimal(string metric, double first, double second)
    {
        Metrics.Add(new PlayerComparisonMetric
        {
            Metric = metric,
            FirstValue = first.ToString("N1"),
            SecondValue = second.ToString("N1"),
            Change = FormatDelta(first, second, "N1")
        });
    }

    private void AddPercent(string metric, double first, double second)
    {
        var difference = second - first;
        Metrics.Add(new PlayerComparisonMetric
        {
            Metric = metric,
            FirstValue = first.ToString("0.0") + "%",
            SecondValue = second.ToString("0.0") + "%",
            Change = difference.ToString("+0.0;-0.0;0.0") + " points"
        });
    }

    private void AddDuration(string metric, double first, double second)
    {
        var difference = second - first;
        Metrics.Add(new PlayerComparisonMetric
        {
            Metric = metric,
            FirstValue = FormatDuration(first),
            SecondValue = FormatDuration(second),
            Change = (difference > 0 ? "+" : difference < 0 ? "-" : string.Empty) +
                     FormatDuration(Math.Abs(difference))
        });
    }

    private static string FormatDelta(double first, double second, string format)
    {
        var difference = second - first;
        var percent = Math.Abs(first) < 0.0001 ? 0.0 : difference * 100.0 / first;
        var differenceText = difference > 0
            ? "+" + difference.ToString(format)
            : difference.ToString(format);
        return differenceText +
               " (" + percent.ToString("+0.0;-0.0;0.0") + "%)";
    }

    private static string FormatDuration(double seconds)
    {
        var duration = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{duration.Minutes:00}:{duration.Seconds:00}";
    }
}
