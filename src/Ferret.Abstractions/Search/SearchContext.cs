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
}
