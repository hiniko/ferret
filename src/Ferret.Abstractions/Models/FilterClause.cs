namespace Ferret.Abstractions.Models;

/// <summary>String-keyed filter clause. Field names refer to the entity's property name.</summary>
public sealed record FilterClause
{
    public required string Field { get; init; }
    public FilterOperator Operator { get; init; }
    public string Value { get; init; } = "";
}
