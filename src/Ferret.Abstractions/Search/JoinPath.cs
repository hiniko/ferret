namespace Ferret.Abstractions.Search;

/// <summary>Cardinality of a single join hop relative to the root entity.</summary>
public enum JoinCardinality
{
    OneToMany = 0,
    ManyToOne = 1,
}

/// <summary>One hop in a search-join chain.</summary>
public sealed record JoinHop
{
    public required string TableName { get; init; }
    public required string TableAlias { get; init; }
    public required string ForeignKeyColumn { get; init; }
    public required Type EntityType { get; init; }
    public JoinCardinality Cardinality { get; init; }
    public bool ForeignKeyOwningSide { get; init; }
    public string? Schema { get; init; }

    /// <summary>The single key column of the table this hop joins into (<see cref="EntityType"/>).
    /// Used as the join target for N:1 references and as the link column for downstream hops.
    /// Defaults to <c>"id"</c>.</summary>
    public string ReferencedKeyColumn { get; init; } = "id";

    /// <summary>Raw SQL condition on this hop's table with <c>{c}</c> as the alias placeholder.
    /// From <c>[SearchJoin(Where = ...)]</c>; applied by query-time join backends.</summary>
    public string? Where { get; init; }
}

/// <summary>Ordered chain of joins from root entity to a searchable property.</summary>
public sealed record JoinPath
{
    public IReadOnlyList<JoinHop> Hops { get; init; } = [];
    public int Depth => Hops.Count;
    public bool IsDirect => Hops.Count == 0;
}
