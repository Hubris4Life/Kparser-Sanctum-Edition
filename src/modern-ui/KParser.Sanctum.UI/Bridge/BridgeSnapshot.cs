namespace KParser.Sanctum.UI.Bridge;

internal sealed class BridgeSnapshot
{
    public int Protocol { get; init; }
    public string Type { get; init; } = string.Empty;
    public string GeneratedUtc { get; init; } = string.Empty;
    public string EngineVersion { get; init; } = string.Empty;
    public string ParseMode { get; init; } = string.Empty;
    public string Report { get; init; } = "damageDealt";
    public string CombatantScope { get; init; } = "all";
    public string DisplayMode { get; init; } = "summary";
    public string GroupMode { get; init; } = "player";
    public bool ParserRunning { get; init; }
    public bool DatabaseOpen { get; init; }
    public BridgeEncounter? Encounter { get; init; }
    public BridgeReportColumns Columns { get; init; } = new();
    public List<BridgeEncounterFilter> Filters { get; init; } = [];
    public List<BridgeCombatantFilter> CombatantFilters { get; init; } = [];
    public List<BridgeCombatant> Combatants { get; init; } = [];
    public string? Error { get; init; }
}

internal sealed class BridgeReportColumns
{
    public string Name { get; init; } = "Combatant";
    public string Secondary { get; init; } = "Job";
    public string Primary { get; init; } = "Damage";
    public string Share { get; init; } = "Share";
    public string Rate { get; init; } = "DPS";
    public string Detail1 { get; init; } = "Melee";
    public string Detail2 { get; init; } = "Weapon skills";
    public string Detail3 { get; init; } = "Magic";
    public string Detail4 { get; init; } = "Other";
    public string Total { get; init; } = "TOTAL DAMAGE";
    public string TotalRate { get; init; } = "ALLIANCE DPS";
    public string RateSuffix { get; init; } = string.Empty;
}

internal sealed class BridgeEncounter
{
    public int BattleId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Scope { get; init; } = string.Empty;
    public string StartUtc { get; init; } = string.Empty;
    public double DurationSeconds { get; init; }
    public bool IsActive { get; init; }
    public int FightCount { get; init; }
    public int EventCount { get; init; }
    public long TotalDamage { get; init; }
    public double AllianceDps { get; init; }
}

internal sealed class BridgeEncounterFilter
{
    public string Scope { get; init; } = string.Empty;
    public int BattleId { get; init; }
    public string MobName { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
}

internal sealed class BridgeCombatantFilter
{
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
}

internal sealed class BridgeCombatant
{
    public string Key { get; init; } = string.Empty;
    public int Rank { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Job { get; set; } = string.Empty;
    public string CombatantType { get; init; } = string.Empty;
    public bool IsLocalPlayer { get; init; }
    public long Damage { get; init; }
    public double SharePercent { get; init; }
    public double Dps { get; init; }
    public long Melee { get; init; }
    public long WeaponSkills { get; init; }
    public long Magic { get; init; }
    public long Other { get; init; }
    public long MeleeDamage { get; init; }
    public long WeaponSkillDamage { get; init; }
    public long MagicDamage { get; init; }
    public long Ranged { get; init; }
    public long Abilities { get; init; }
    public long Skillchains { get; init; }
    public long AdditionalEffects { get; init; }
    public long Counters { get; init; }
    public long Retaliation { get; init; }
    public long Spikes { get; init; }
    public long PhysicalAttempts { get; init; }
    public long PhysicalHits { get; init; }
    public long PhysicalMisses { get; init; }
    public long CriticalHits { get; init; }
    public string PrimaryText { get; init; } = string.Empty;
    public string ShareText { get; init; } = string.Empty;
    public string RateText { get; init; } = string.Empty;
    public string Detail1Text { get; init; } = string.Empty;
    public string Detail2Text { get; init; } = string.Empty;
    public string Detail3Text { get; init; } = string.Empty;
    public string Detail4Text { get; init; } = string.Empty;
    public string TopAction { get; init; } = string.Empty;
    public string Accuracy { get; init; } = string.Empty;
    public string CriticalRate { get; init; } = string.Empty;
}

internal sealed class BridgeCommandResult
{
    public int Protocol { get; init; }
    public string Type { get; init; } = string.Empty;
    public string Command { get; init; } = string.Empty;
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public bool ParserRunning { get; init; }
    public bool DatabaseOpen { get; init; }
    public string MemoryOffset { get; init; } = string.Empty;
}
