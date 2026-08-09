namespace KParser.Sanctum.UI.Models;

internal sealed class PlayerParseSnapshot
{
    public int DataVersion { get; set; } = 1;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Label { get; set; } = string.Empty;
    public DateTime SavedUtc { get; set; } = DateTime.UtcNow;
    public string PlayerName { get; set; } = string.Empty;
    public string Job { get; set; } = string.Empty;
    public string EncounterName { get; set; } = string.Empty;
    public string EncounterScope { get; set; } = string.Empty;
    public int FightCount { get; set; }
    public int EventCount { get; set; }
    public double DurationSeconds { get; set; }
    public string EngineVersion { get; set; } = string.Empty;
    public long TotalDamage { get; set; }
    public double Dps { get; set; }
    public double SharePercent { get; set; }
    public long MeleeDamage { get; set; }
    public long RangedDamage { get; set; }
    public long WeaponSkillDamage { get; set; }
    public long AbilityDamage { get; set; }
    public long MagicDamage { get; set; }
    public long SkillchainDamage { get; set; }
    public long AdditionalEffectDamage { get; set; }
    public long ReactiveDamage { get; set; }
    public long PhysicalAttempts { get; set; }
    public long PhysicalHits { get; set; }
    public long PhysicalMisses { get; set; }
    public long CriticalHits { get; set; }
    public string TopAction { get; set; } = string.Empty;

    public double AccuracyPercent => PhysicalAttempts == 0
        ? 0.0
        : (double)PhysicalHits * 100.0 / PhysicalAttempts;

    public double CriticalRatePercent => PhysicalHits == 0
        ? 0.0
        : (double)CriticalHits * 100.0 / PhysicalHits;

    public string DisplayLabel => $"{PlayerName} · {Label} · {SavedUtc.ToLocalTime():g}";
}

internal sealed class PlayerComparisonMetric
{
    public required string Metric { get; init; }
    public required string FirstValue { get; init; }
    public required string SecondValue { get; init; }
    public required string Change { get; init; }
}
