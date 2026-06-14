namespace Ferret.Abstractions.Models;

public sealed record SortClause
{
    public required string Field { get; init; }
    public SortDirection Direction { get; init; }
}
