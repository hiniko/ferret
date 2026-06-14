namespace Ferret.Abstractions.Models;

/// <summary>Per-field match metadata returned when <c>PagedQuery.IncludeMatchInfo</c> is set.</summary>
public sealed record SearchMatchInfo
{
    public required string Field { get; init; }
    public double Score { get; init; }
    public object? MatchingChildId { get; init; }
}
