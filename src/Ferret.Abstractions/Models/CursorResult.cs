namespace Ferret.Abstractions.Models;

public sealed record CursorResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public int Limit { get; init; }
    public string? NextCursor { get; init; }
    public string? PrevCursor { get; init; }
    public bool HasMore { get; init; }
    public bool HasPrev { get; init; }
    public SearchMatchInfo? MatchInfo { get; init; }
}
