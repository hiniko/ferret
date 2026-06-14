using Ferret.Abstractions;
using Ferret.AspNetCore.Binding;
using Microsoft.AspNetCore.Mvc;

namespace Ferret.AspNetCore;

public abstract record PaginationParameters
{
    [ModelBinder(Name = "limit")]
    public int? Limit { get; init; }

    [ModelBinder(Name = "q")]
    public string? Q { get; init; }

    [ModelBinder(Name = "fields")]
    public IReadOnlyList<string> Fields { get; init; } = [];

    [ModelBinder(Name = "match_info")]
    public bool MatchInfo { get; init; }

    [ModelBinder(Name = "sort", BinderType = typeof(SortClauseListBinder))]
    public IReadOnlyList<SortClause> Sort { get; init; } = [];

    [ModelBinder(Name = "filter", BinderType = typeof(FilterClauseListBinder))]
    public IReadOnlyList<FilterClause> Filter { get; init; } = [];
}
