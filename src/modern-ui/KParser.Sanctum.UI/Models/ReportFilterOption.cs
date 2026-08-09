namespace KParser.Sanctum.UI.Models;

internal sealed class ReportFilterOption
{
    public required string Key { get; init; }
    public required string Label { get; init; }

    public override string ToString() => Label;
}
