// Created for KParser - Sanctum Edition, 2026. See /MODIFICATIONS.md.
using System;
using System.Collections.Generic;

namespace WaywardGamers.KParser.Bridge
{
    internal sealed class SanctumBridgeRequest
    {
        public int Protocol { get; set; }
        public string Type { get; set; }
        public string Command { get; set; }
        public string TargetPlayer { get; set; }
        public string Scope { get; set; }
        public int BattleId { get; set; }
        public string MobName { get; set; }
        public string Report { get; set; }
        public string CombatantScope { get; set; }
        public string DisplayMode { get; set; }
        public string GroupMode { get; set; }
        public string SearchText { get; set; }
        public bool ExcludeCommonDrops { get; set; }
        public string ServerProfile { get; set; }
        public string PetMappingPath { get; set; }
        public string LocalPlayerName { get; set; }
    }

    public sealed class SanctumEngineCommand
    {
        public string Name { get; set; }
        public string TargetPlayer { get; set; }
    }

    public sealed class SanctumEngineCommandResult
    {
        public SanctumEngineCommandResult()
        {
            Protocol = 1;
            Type = "commandResult";
            Command = string.Empty;
            Message = string.Empty;
            MemoryOffset = string.Empty;
        }

        public int Protocol { get; set; }
        public string Type { get; set; }
        public string Command { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
        public bool ParserRunning { get; set; }
        public bool DatabaseOpen { get; set; }
        public string MemoryOffset { get; set; }
    }

    internal sealed class SanctumBridgeSnapshot
    {
        public SanctumBridgeSnapshot()
        {
            Protocol = 1;
            Type = "snapshot";
            GeneratedUtc = DateTime.UtcNow.ToString("o");
            EngineVersion = string.Empty;
            ParseMode = string.Empty;
            Report = "damageDealt";
            CombatantScope = "all";
            DisplayMode = "summary";
            GroupMode = "player";
            ServerProfile = "sanctum";
            PetOwnershipMode = "SanctumChat";
            Columns = SanctumReportColumnsSnapshot.CreateDamageDealt();
            Filters = new List<SanctumEncounterFilterSnapshot>();
            CombatantFilters = new List<SanctumCombatantFilterSnapshot>();
            Combatants = new List<SanctumCombatantSnapshot>();
        }

        public int Protocol { get; set; }
        public string Type { get; set; }
        public string GeneratedUtc { get; set; }
        public string EngineVersion { get; set; }
        public string ParseMode { get; set; }
        public string Report { get; set; }
        public string CombatantScope { get; set; }
        public string DisplayMode { get; set; }
        public string GroupMode { get; set; }
        public string ServerProfile { get; set; }
        public string PetOwnershipMode { get; set; }
        public bool EstimatedDotsAvailable { get; set; }
        public bool ParserRunning { get; set; }
        public bool ClientLoggedIn { get; set; }
        public bool DatabaseOpen { get; set; }
        public SanctumEncounterSnapshot Encounter { get; set; }
        public SanctumReportColumnsSnapshot Columns { get; set; }
        public List<SanctumEncounterFilterSnapshot> Filters { get; set; }
        public List<SanctumCombatantFilterSnapshot> CombatantFilters { get; set; }
        public List<SanctumCombatantSnapshot> Combatants { get; set; }
        public string Error { get; set; }
    }

    internal sealed class SanctumReportColumnsSnapshot
    {
        public string Name { get; set; }
        public string Secondary { get; set; }
        public string Primary { get; set; }
        public string Share { get; set; }
        public string Rate { get; set; }
        public string Detail1 { get; set; }
        public string Detail2 { get; set; }
        public string Detail3 { get; set; }
        public string Detail4 { get; set; }
        public string Total { get; set; }
        public string TotalRate { get; set; }
        public string RateSuffix { get; set; }

        public static SanctumReportColumnsSnapshot CreateDamageDealt()
        {
            return new SanctumReportColumnsSnapshot
            {
                Name = "Combatant",
                Secondary = "Job",
                Primary = "Damage",
                Share = "Share",
                Rate = "DPS",
                Detail1 = "Melee",
                Detail2 = "Weapon skills",
                Detail3 = "Magic",
                Detail4 = "Other",
                Total = "TOTAL DAMAGE",
                TotalRate = "ALLIANCE DPS",
                RateSuffix = string.Empty
            };
        }
    }

    internal sealed class SanctumEncounterFilterSnapshot
    {
        public string Scope { get; set; }
        public int BattleId { get; set; }
        public string MobName { get; set; }
        public string Label { get; set; }
    }

    internal sealed class SanctumEncounterSnapshot
    {
        public int BattleId { get; set; }
        public string Name { get; set; }
        public string Scope { get; set; }
        public string StartUtc { get; set; }
        public double DurationSeconds { get; set; }
        public bool IsActive { get; set; }
        public int FightCount { get; set; }
        public int EventCount { get; set; }
        public long TotalDamage { get; set; }
        public double AllianceDps { get; set; }
    }

    internal sealed class SanctumCombatantFilterSnapshot
    {
        public string Key { get; set; }
        public string Label { get; set; }
    }

    internal sealed class SanctumCombatantSnapshot
    {
        public string Key { get; set; }
        public int Rank { get; set; }
        public string Name { get; set; }
        public string Job { get; set; }
        public string CombatantType { get; set; }
        public bool IsLocalPlayer { get; set; }
        public long Damage { get; set; }
        public double SharePercent { get; set; }
        public double Dps { get; set; }
        public long Melee { get; set; }
        public long WeaponSkills { get; set; }
        public long Magic { get; set; }
        public long Other { get; set; }
        public long MeleeDamage { get; set; }
        public long WeaponSkillDamage { get; set; }
        public long MagicDamage { get; set; }
        public long Ranged { get; set; }
        public long Abilities { get; set; }
        public long Skillchains { get; set; }
        public long AdditionalEffects { get; set; }
        public long Counters { get; set; }
        public long Retaliation { get; set; }
        public long Spikes { get; set; }
        public long PhysicalAttempts { get; set; }
        public long PhysicalHits { get; set; }
        public long PhysicalMisses { get; set; }
        public long CriticalHits { get; set; }
        public long ExtraAttackRounds { get; set; }
        public string PrimaryText { get; set; }
        public string ShareText { get; set; }
        public string RateText { get; set; }
        public string Detail1Text { get; set; }
        public string Detail2Text { get; set; }
        public string Detail3Text { get; set; }
        public string Detail4Text { get; set; }
        public string TopAction { get; set; }
        public string Accuracy { get; set; }
        public string CriticalRate { get; set; }
    }
}
