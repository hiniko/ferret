namespace Ferret.Abstractions.Models;

public sealed record OffsetResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public int Limit { get; init; }
    public int Page { get; init; }
    public int TotalCount { get; init; }
    public bool HasMore { get; init; }
    public bool HasPrev { get; init; }
    public SearchMatchInfo? MatchInfo { get; init; }
}
