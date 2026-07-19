using System.Linq.Expressions;

namespace Ferret.Abstractions.Models;

/// <summary>One entity's participation in a multisearch: what to search and how to shape hits.</summary>
public sealed class MultiSearchEntityDefinition
{
    public required Type EntityType { get; init; }

    public required Type HitType { get; init; }

    /// <summary>An <c>Expression&lt;Func&lt;TEntity, THit&gt;&gt;</c> whose body is a new-expression or
    /// member-init over plain entity property accesses and constants.</summary>
    public required LambdaExpression Projection { get; init; }

    public IReadOnlyList<FilterClause> Filters { get; init; } = [];
}

/// <summary>A validated multisearch request. Build via <see cref="MultiSearchQueryBuilder"/>.</summary>
public sealed class MultiSearchQuery
{
    public required string Search { get; init; }

    /// <summary>Top-k cap applied per entity group, not overall.</summary>
    public int PerEntityLimit { get; init; } = 5;

    public required IReadOnlyList<MultiSearchEntityDefinition> Entities { get; init; }

    /// <summary>Optional subset filter over <see cref="Entities"/>, matched case-insensitively against
    /// the engine-side entity name (the table name). Null or empty means all. The engine validates the
    /// names — the registry that knows them is not visible at this layer.</summary>
    public IReadOnlyList<string>? Types { get; init; }
}

/// <summary>Per-entity options within a multisearch registration.</summary>
public sealed class MultiSearchEntityOptions
{
    private readonly List<FilterClause> _filters = [];

    public IReadOnlyList<FilterClause> Filters => _filters;

    public MultiSearchEntityOptions Filter(FilterClause clause)
    {
        ArgumentNullException.ThrowIfNull(clause);
        _filters.Add(clause);
        return this;
    }
}
