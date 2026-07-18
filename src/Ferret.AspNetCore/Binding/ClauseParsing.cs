namespace Ferret.AspNetCore.Binding;

/// <summary>
/// Pure parse rules for compact filter/sort query parameters, shared by MVC and minimal-API binders.
/// Filter wire format: <c>field:op:value</c>. Sort wire format: <c>field</c> or <c>field:direction</c>.
/// </summary>
public static class ClauseParsing
{
    public static IReadOnlyList<FilterClause> ParseFilters(IEnumerable<string?> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var clauses = new List<FilterClause>();
        foreach (var raw in values)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var parts = raw.Split(':', 3);
            if (parts.Length < 2) continue;

            var op = parts[1].ToLowerInvariant() switch
            {
                "eq" => FilterOperator.Equals,
                "neq" => FilterOperator.NotEquals,
                "contains" => FilterOperator.Contains,
                "gt" => FilterOperator.GreaterThan,
                "gte" => FilterOperator.GreaterThanOrEqual,
                "lt" => FilterOperator.LessThan,
                "lte" => FilterOperator.LessThanOrEqual,
                "in" => FilterOperator.In,
                "isnull" => FilterOperator.IsNull,
                "notnull" => FilterOperator.NotNull,
                _ => (FilterOperator?)null,
            };
            if (op is null) continue;

            // Value-less operators accept "field:isnull" (2 parts) or "field:isnull:" (3 parts, ignored value).
            var valueless = op is FilterOperator.IsNull or FilterOperator.NotNull;
            if (parts.Length != 3 && !valueless) continue;

            clauses.Add(new FilterClause { Field = parts[0], Operator = op.Value, Value = valueless ? "" : parts[2] });
        }

        return clauses;
    }

    public static IReadOnlyList<SortClause> ParseSorts(IEnumerable<string?> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var clauses = new List<SortClause>();
        foreach (var raw in values)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var parts = raw.Split(':', 2);
            var field = parts[0];
            var dir = parts.Length > 1 && parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase)
                ? SortDirection.Descending
                : SortDirection.Ascending;
            clauses.Add(new SortClause { Field = field, Direction = dir });
        }

        return clauses;
    }
}
