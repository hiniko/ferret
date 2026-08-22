namespace Ferret.Abstractions.Search;

/// <summary>Per-query context passed to <see cref="ISearchBackend.BuildRankingQuery"/>.</summary>
public sealed record SearchContext
{
    public required IReadOnlyList<SearchablePropertyInfo> Properties { get; init; }
    public required string SearchTerm { get; init; }
    public float[]? SearchVector { get; init; }
    public string IdColumn { get; init; } = "id";
    public string QuotedTable { get; init; } = "";
    public string CandidateIdsParameterName { get; init; } = "@candidate_ids";
    public bool HasCandidateIds { get; init; }
    public IReadOnlyList<string>? KeyColumns { get; init; }
    public IReadOnlyList<string>? CandidateKeyParameterNames { get; init; }
    public int Limit { get; init; } = int.MaxValue;
    public int Offset { get; init; }

    /// <summary>
    /// Parameterised SQL predicate (aliased against the base table as <c>e</c>) compiled from
    /// <see cref="Ferret.Abstractions.Models.PagedQuery{T,TKey}.Filter"/>. References its
    /// parameters by name (e.g. <c>@f0</c>); the caller binds their values separately, the same
    /// way <see cref="CandidateIdsParameterName"/> is bound. When set, the candidates CTE
    /// selects directly from the base table with this predicate instead of (or in addition to,
    /// when <see cref="HasCandidateIds"/> is also true) unnesting a materialised key array.
    /// Null when no filter clauses are present.
    /// </summary>
    public string? CandidatePredicateSql { get; init; }
}
