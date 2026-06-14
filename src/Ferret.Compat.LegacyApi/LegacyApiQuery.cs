using Ferret.AspNetCore.Binding;
using Microsoft.AspNetCore.Mvc;

namespace Ferret.Compat.LegacyApi;

/// <summary>
/// Legacy-API wire-shape query DTO. Mirrors the original backend
/// <c>ApiPagedQueryParams</c> field names (<c>page</c>, <c>page_size</c>, <c>search</c>,
/// <c>search_fields</c>, <c>include_match_info</c>, <c>include_hidden</c>) so existing
/// HTTP clients can keep their query strings unchanged while the server runs on Ferret v1.
/// </summary>
public sealed record LegacyApiQuery
{
    [ModelBinder(Name = "page")]                public int? Page { get; init; }
    [ModelBinder(Name = "page_size")]           public int? PageSize { get; init; }
    [ModelBinder(Name = "search")]              public string? Search { get; init; }
    [ModelBinder(Name = "search_fields")]       public IReadOnlyList<string> SearchFields { get; init; } = [];
    [ModelBinder(Name = "include_match_info")]  public bool IncludeMatchInfo { get; init; }
    [ModelBinder(Name = "include_hidden")]      public bool IncludeHidden { get; init; }

    [ModelBinder(Name = "sort", BinderType = typeof(SortClauseListBinder))]
    public IReadOnlyList<SortClause> Sort { get; init; } = [];

    [ModelBinder(Name = "filter", BinderType = typeof(FilterClauseListBinder))]
    public IReadOnlyList<FilterClause> Filter { get; init; } = [];

    /// <summary>
    /// Projects the legacy wire-shape query into the engine's <see cref="PagedQuery{T, TKey}"/>.
    /// Always returns an offset-mode query with <c>RequestTotalCount = true</c>, matching the
    /// original backend semantics.
    /// </summary>
    public PagedQuery<T, TKey> ToPagedQuery<T, TKey>(PaginationDefaults defaults)
        where T : class where TKey : notnull
    {
        var limit = PageSize ?? defaults.DefaultLimit;
        if (limit > defaults.MaxLimit)
            throw new InvalidOperationException($"page_size exceeds maximum ({defaults.MaxLimit})");
        return new PagedQuery<T, TKey>
        {
            Mode = PaginationMode.Offset,
            Limit = limit,
            Search = Search,
            SearchFields = SearchFields,
            IncludeMatchInfo = IncludeMatchInfo,
            Sort = Sort,
            Filter = Filter,
            Page = Page,
            RequestTotalCount = true,
        };
    }
}
