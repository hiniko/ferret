using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Ferret.AspNetCore.Binding;

internal static class QueryStringQueryBinder
{
    public static OffsetApiQuery BindOffset(IQueryCollection query)
    {
        ArgumentNullException.ThrowIfNull(query);

        return new OffsetApiQuery
        {
            Q = String(query, "q"),
            Fields = StringList(query, "fields"),
            MatchInfo = Bool(query, "match_info") ?? false,
            Limit = Int(query, "limit"),
            Sort = ClauseParsing.ParseSorts(Values(query, "sort")),
            Filter = ClauseParsing.ParseFilters(Values(query, "filter")),
            Page = Int(query, "page"),
            Count = Bool(query, "count") ?? true,
        };
    }

    public static CursorApiQuery BindCursor(IQueryCollection query)
    {
        ArgumentNullException.ThrowIfNull(query);

        return new CursorApiQuery
        {
            Q = String(query, "q"),
            Fields = StringList(query, "fields"),
            MatchInfo = Bool(query, "match_info") ?? false,
            Limit = Int(query, "limit"),
            Sort = ClauseParsing.ParseSorts(Values(query, "sort")),
            Filter = ClauseParsing.ParseFilters(Values(query, "filter")),
            After = String(query, "after"),
            Before = String(query, "before"),
        };
    }

    private static string? String(IQueryCollection query, string key)
    {
        var value = query[key].ToString();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static IReadOnlyList<string> StringList(IQueryCollection query, string key)
        => query[key].Where(v => !string.IsNullOrEmpty(v)).Select(v => v!).ToArray();

    private static IEnumerable<string?> Values(IQueryCollection query, string key) => query[key];

    private static int? Int(IQueryCollection query, string key)
        => int.TryParse(query[key].ToString(), out var value) ? value : null;

    private static bool? Bool(IQueryCollection query, string key)
        => bool.TryParse(query[key].ToString(), out var value) ? value : null;
}
