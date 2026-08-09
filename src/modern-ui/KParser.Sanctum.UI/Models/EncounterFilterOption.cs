namespace KParser.Sanctum.UI.Models;

internal sealed class EncounterFilterOption
{
    public string Scope { get; init; } = "all";
    public int BattleId { get; init; }
    public string? MobName { get; init; }
    public string Label { get; init; } = string.Empty;

    public string Key => Scope + ":" + BattleId + ":" + (MobName ?? string.Empty);

    public override string ToString() => Label;
}
