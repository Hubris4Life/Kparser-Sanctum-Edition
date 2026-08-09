using System.ComponentModel;

namespace KParser.Sanctum.UI.Models;

internal sealed class CombatantRow : INotifyPropertyChanged
{
    public string Key { get; set; } = string.Empty;
    public required int Rank { get; set; }
    public required string Name { get; set; }
    public required string Job { get; set; }
    public string CombatantType { get; set; } = string.Empty;
    public bool IsLocalPlayer { get; set; }
    public bool IsActionDetail { get; set; }
    public required long Damage { get; set; }
    public required double Share { get; set; }
    public required double Dps { get; set; }
    public long? Melee { get; set; }
    public long? WeaponSkills { get; set; }
    public long? Magic { get; set; }
    public long? Other { get; set; }
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
    public string PrimaryText { get; set; } = string.Empty;
    public string ShareText { get; set; } = string.Empty;
    public string RateText { get; set; } = string.Empty;
    public string Detail1Text { get; set; } = string.Empty;
    public string Detail2Text { get; set; } = string.Empty;
    public string Detail3Text { get; set; } = string.Empty;
    public string Detail4Text { get; set; } = string.Empty;
    public string RateSuffix { get; set; } = string.Empty;
    public string TopAction { get; set; } = "No action summary available";
    public string Accuracy { get; set; } = "—";
    public string CriticalRate { get; set; } = "—";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string DamageDisplay => TextOrDefault(PrimaryText, Damage.ToString("N0"));
    public string ShareDisplay => TextOrDefault(ShareText, Share.ToString("0.0") + "%");
    public string DpsDisplay => TextOrDefault(RateText, Dps.ToString("N1") + RateSuffix);
    public string MeleeDisplay => TextOrDefault(Detail1Text, FormatAmount(Melee));
    public string WeaponSkillsDisplay => TextOrDefault(Detail2Text, FormatAmount(WeaponSkills));
    public string MagicDisplay => TextOrDefault(Detail3Text, FormatAmount(Magic));
    public string OtherDisplay => TextOrDefault(Detail4Text, FormatAmount(Other));
    public string AccuracyDisplay => FormatLabeledDetail(Accuracy, "Accuracy:");
    public string SourceBreakdownDisplay => string.Join(
        "  ·  ",
        "Melee " + FormatAmount(MeleeDamage),
        "Ranged " + FormatAmount(Ranged),
        "WS " + FormatAmount(WeaponSkillDamage),
        "Magic " + FormatAmount(MagicDamage),
        "Abilities " + FormatAmount(Abilities),
        "Skillchains " + FormatAmount(Skillchains),
        "Additional " + FormatAmount(AdditionalEffects),
        "Reactive " + FormatAmount(Counters + Retaliation + Spikes));
    public string PhysicalBreakdownDisplay => PhysicalAttempts > 0
        ? $"Physical: {PhysicalHits:N0}/{PhysicalAttempts:N0} hits  ·  {PhysicalMisses:N0} misses  ·  {CriticalHits:N0} criticals"
        : "Physical hit details unavailable";

    public bool ContentEquals(CombatantRow other)
    {
        if (Key != other.Key || Rank != other.Rank || Name != other.Name || Job != other.Job ||
            CombatantType != other.CombatantType || IsLocalPlayer != other.IsLocalPlayer ||
            IsActionDetail != other.IsActionDetail ||
            Damage != other.Damage || Share != other.Share || Dps != other.Dps ||
            Melee != other.Melee || WeaponSkills != other.WeaponSkills ||
            Magic != other.Magic || Other != other.Other ||
            MeleeDamage != other.MeleeDamage ||
            WeaponSkillDamage != other.WeaponSkillDamage ||
            MagicDamage != other.MagicDamage || Ranged != other.Ranged ||
            Abilities != other.Abilities || Skillchains != other.Skillchains ||
            AdditionalEffects != other.AdditionalEffects || Counters != other.Counters ||
            Retaliation != other.Retaliation || Spikes != other.Spikes ||
            PhysicalAttempts != other.PhysicalAttempts ||
            PhysicalHits != other.PhysicalHits ||
            PhysicalMisses != other.PhysicalMisses || CriticalHits != other.CriticalHits ||
            PrimaryText != other.PrimaryText || ShareText != other.ShareText ||
            RateText != other.RateText || Detail1Text != other.Detail1Text ||
            Detail2Text != other.Detail2Text || Detail3Text != other.Detail3Text ||
            Detail4Text != other.Detail4Text ||
            RateSuffix != other.RateSuffix ||
            TopAction != other.TopAction || Accuracy != other.Accuracy ||
            CriticalRate != other.CriticalRate)
        {
            return false;
        }

        return true;
    }

    public void UpdateFrom(CombatantRow other)
    {
        Key = other.Key;
        Rank = other.Rank;
        Name = other.Name;
        Job = other.Job;
        CombatantType = other.CombatantType;
        IsLocalPlayer = other.IsLocalPlayer;
        IsActionDetail = other.IsActionDetail;
        Damage = other.Damage;
        Share = other.Share;
        Dps = other.Dps;
        Melee = other.Melee;
        WeaponSkills = other.WeaponSkills;
        Magic = other.Magic;
        Other = other.Other;
        MeleeDamage = other.MeleeDamage;
        WeaponSkillDamage = other.WeaponSkillDamage;
        MagicDamage = other.MagicDamage;
        Ranged = other.Ranged;
        Abilities = other.Abilities;
        Skillchains = other.Skillchains;
        AdditionalEffects = other.AdditionalEffects;
        Counters = other.Counters;
        Retaliation = other.Retaliation;
        Spikes = other.Spikes;
        PhysicalAttempts = other.PhysicalAttempts;
        PhysicalHits = other.PhysicalHits;
        PhysicalMisses = other.PhysicalMisses;
        CriticalHits = other.CriticalHits;
        PrimaryText = other.PrimaryText;
        ShareText = other.ShareText;
        RateText = other.RateText;
        Detail1Text = other.Detail1Text;
        Detail2Text = other.Detail2Text;
        Detail3Text = other.Detail3Text;
        Detail4Text = other.Detail4Text;
        RateSuffix = other.RateSuffix;
        TopAction = other.TopAction;
        Accuracy = other.Accuracy;
        CriticalRate = other.CriticalRate;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    private static string FormatAmount(long? amount) =>
        amount.HasValue && amount.Value > 0 ? amount.Value.ToString("N0") : "—";

    private static string FormatAmount(long amount) =>
        amount > 0 ? amount.ToString("N0") : "—";

    private static string TextOrDefault(string text, string fallback) =>
        string.IsNullOrWhiteSpace(text) ? fallback : text;

    private static string FormatLabeledDetail(string detail, string label)
    {
        if (string.IsNullOrWhiteSpace(detail))
            return "—";

        var value = detail.StartsWith(label, StringComparison.OrdinalIgnoreCase)
            ? detail[label.Length..].Trim()
            : detail.Trim();

        return value == "-" ? "—" : value;
    }
}
