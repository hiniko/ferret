using Ferret.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace Ferret.AspNetCore;

public sealed record CursorApiQuery : PaginationParameters
{
    [ModelBinder(Name = "after")]
    public string? After { get; init; }

    [ModelBinder(Name = "before")]
    public string? Before { get; init; }

    public PagedQuery<T, TKey> ToPagedQuery<T, TKey>(PaginationDefaults defaults)
        where T : class where TKey : notnull
        => ToPagedQuery<T, TKey>(defaults, []);

    // authoritativeFilters: server-enforced clauses, applied on top of the caller's ?filter= and
    // authoritative — a caller clause on the same field is replaced, not combined, so a request
    // cannot widen or escape a tenant / soft-delete / permission boundary.
    public PagedQuery<T, TKey> ToPagedQuery<T, TKey>(
        PaginationDefaults defaults,
        IReadOnlyList<FilterClause> authoritativeFilters)
        where T : class where TKey : notnull
    {
        if (After is not null && Before is not null)
            throw new InvalidOperationException("specify one of after, before");

        var limit = Limit ?? defaults.DefaultLimit;
        if (limit > defaults.MaxLimit)
            throw new InvalidOperationException($"limit exceeds maximum ({defaults.MaxLimit})");

        var direction = After is not null ? CursorDirection.Forward
                      : Before is not null ? CursorDirection.Backward
                      : CursorDirection.None;

        return new PagedQuery<T, TKey>
        {
            Mode = PaginationMode.Cursor,
            Limit = limit,
            Search = Q,
            SearchFields = Fields,
            IncludeMatchInfo = MatchInfo,
            Sort = Sort,
            Filter = Filter,
            AuthoritativeFilter = authoritativeFilters,
            Cursor = After ?? Before,
            CursorDirection = direction,
        };
    }
}
