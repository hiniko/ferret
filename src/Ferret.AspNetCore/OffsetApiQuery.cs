using Ferret.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace Ferret.AspNetCore;

public sealed record OffsetApiQuery : PaginationParameters
{
    [ModelBinder(Name = "page")]
    public int? Page { get; init; }

    [ModelBinder(Name = "count")]
    public bool Count { get; init; } = true;

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
        var limit = Limit ?? defaults.DefaultLimit;
        if (limit > defaults.MaxLimit)
            throw new InvalidOperationException($"limit exceeds maximum ({defaults.MaxLimit})");
        return new PagedQuery<T, TKey>
        {
            Mode = PaginationMode.Offset,
            Limit = limit,
            Search = Q,
            SearchFields = Fields,
            Sort = Sort,
            Filter = Filter,
            AuthoritativeFilter = authoritativeFilters,
            Page = Page,
            RequestTotalCount = Count,
        };
    }
}
