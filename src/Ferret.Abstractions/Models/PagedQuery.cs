using Ferret.Abstractions.Attributes;

namespace Ferret.Abstractions.Models;

/// <summary>
/// Service-layer query parameters used by the engine and SQL builders. Carries
/// pagination mode, search, sort, filter, and match-info flags. Constructed by the
/// API-layer query DTOs (<c>OffsetApiQuery</c>, <c>CursorApiQuery</c>) and consumed
/// by <c>IQueryEngine.SearchOffsetAsync</c> / <c>SearchCursorAsync</c>.
/// </summary>
public sealed record PagedQuery<T, TKey>
    where T : class
    where TKey : notnull
{
    /// <summary>Which pagination shape this query uses. The engine dispatches on it.</summary>
    public PaginationMode Mode { get; init; } = PaginationMode.Offset;

    /// <summary>Maximum rows in the result page. Default 25.</summary>
    public int Limit { get; init; } = 25;

    /// <summary>Search term. Routed to one or more configured backends.</summary>
    public string? Search { get; init; }

    public IReadOnlyList<string> SearchFields { get; init; } = [];
    public bool IncludeMatchInfo { get; init; }

    public IReadOnlyList<SortClause> Sort { get; init; } = [];
    public IReadOnlyList<FilterClause> Filter { get; init; } = [];

    /// <summary>
    /// Opaque cursor token consumed when <see cref="Mode"/> is <see cref="PaginationMode.Cursor"/>.
    /// Null on the first cursor-mode request and in offset mode.
    /// </summary>
    public string? Cursor { get; init; }

    /// <summary>
    /// Direction the cursor is consumed. <see cref="CursorDirection.None"/> indicates the
    /// caller did not supply a cursor (first cursor-mode request, or this is offset mode).
    /// </summary>
    public CursorDirection CursorDirection { get; init; } = CursorDirection.None;

    /// <summary>
    /// Zero-based page index for <see cref="PaginationMode.Offset"/>. Null means the first
    /// page. Ignored in cursor mode.
    /// </summary>
    public int? Page { get; init; }

    /// <summary>
    /// If true, the engine populates <c>TotalCount</c> on the offset result. Default false
    /// (callers requesting offset mode typically set this to true via the API binder).
    /// </summary>
    public bool RequestTotalCount { get; init; }

    /// <summary>Force a single search backend for this query, bypassing hybrid fusion. Null = default routing.</summary>
    public SearchBackend? Backend { get; init; }

    /// <summary>
    /// Externally-computed candidate restriction for search mode: only rows whose key is in
    /// this list are ranked. Combined by intersection when <see cref="Filter"/> clauses are
    /// also present; an empty list short-circuits to an empty result. Ignored when
    /// <see cref="Search"/> is not set. For callers whose filtering cannot be expressed as
    /// <see cref="FilterClause"/> (e.g. ORM-composed predicates): materialise the filtered
    /// keys and pass them here.
    /// </summary>
    public IReadOnlyList<TKey>? CandidateKeys { get; init; }
}
