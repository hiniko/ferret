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
            IncludeMatchInfo = MatchInfo,
            Sort = Sort,
            Filter = Filter,
            Page = Page,
            RequestTotalCount = Count,
        };
    }
}
