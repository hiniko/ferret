using System.Globalization;
using System.Text;

namespace Ferret.Core.Sql;

public static class PagedSqlBuilder
{
    public static SqlFragment CompileFilter(FilterClause filter, EntityMetadata meta, int parameterIndex)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(meta);

        if (!meta.Filterable.TryGetValue(filter.Field, out var rule))
        {
            throw new InvalidOperationException(
                $"Field '{filter.Field}' is not Filterable on table '{meta.TableName}'.");
        }
        if (rule.Operators.Length > 0 && Array.IndexOf(rule.Operators, filter.Operator) < 0)
        {
            throw new InvalidOperationException(
                $"Operator {filter.Operator} not allowed on field '{filter.Field}'.");
        }

        var col = meta.Dialect.QuoteIdentifier(meta.ColumnByPropertyName[filter.Field]);
        var clr = meta.ClrTypeByPropertyName[filter.Field];
        var paramName = $"@p{parameterIndex}";

        if (filter.Operator is FilterOperator.IsNull or FilterOperator.NotNull)
        {
            var nullSql = filter.Operator == FilterOperator.IsNull ? $"{col} IS NULL" : $"{col} IS NOT NULL";
            return new SqlFragment(nullSql, [], parameterIndex);
        }

        if (filter.Operator == FilterOperator.In)
        {
            var raws = filter.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (raws.Length == 0)
                throw new ArgumentException($"Filter operator 'in' requires at least one value on field '{filter.Field}'.");
            var arr = BuildTypedArray(raws, clr);
            return new SqlFragment($"{col} = {meta.Dialect.ArrayParameter(paramName)}", [arr], parameterIndex);
        }

        var value = ConvertFilterValue(filter.Value, clr);
        // Contains is a substring match: escape ILIKE metacharacters in the user value so
        // "50%" matches literally instead of acting as a wildcard (default escape is backslash).
        if (filter.Operator == FilterOperator.Contains && value is string sv)
            value = sv.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        var sql = filter.Operator switch
        {
            FilterOperator.Equals             => $"{col} = {paramName}",
            FilterOperator.NotEquals          => $"{col} <> {paramName}",
            FilterOperator.GreaterThan        => $"{col} > {paramName}",
            FilterOperator.GreaterThanOrEqual => $"{col} >= {paramName}",
            FilterOperator.LessThan           => $"{col} < {paramName}",
            FilterOperator.LessThanOrEqual    => $"{col} <= {paramName}",
            FilterOperator.Contains           => $"{col} ILIKE '%' || {paramName} || '%'",
            _ => throw new NotSupportedException($"Filter operator {filter.Operator} not supported.")
        };

        return new SqlFragment(sql, [value], parameterIndex);
    }

    private static Array BuildTypedArray(string[] raws, Type clrType)
    {
        var u = Nullable.GetUnderlyingType(clrType) ?? clrType;
        var arr = Array.CreateInstance(u, raws.Length);
        for (var i = 0; i < raws.Length; i++)
            arr.SetValue(ConvertFilterValue(raws[i], u), i);
        return arr;
    }

    public static SqlFragment CompileSort(SortClause sort, EntityMetadata meta)
    {
        ArgumentNullException.ThrowIfNull(sort);
        ArgumentNullException.ThrowIfNull(meta);

        if (!meta.Sortable.Contains(sort.Field))
        {
            throw new InvalidOperationException(
                $"Field '{sort.Field}' is not Sortable on table '{meta.TableName}'.");
        }

        var col = meta.Dialect.QuoteIdentifier(meta.ColumnByPropertyName[sort.Field]);
        var dir = sort.Direction == SortDirection.Descending ? "DESC" : "ASC";
        return new SqlFragment($"{col} {dir}", []);
    }

    public static SqlFragment BuildSelectIdsAndCount(
        EntityMetadata meta,
        IReadOnlyList<SqlFragment> filterFragments,
        IReadOnlyList<SqlFragment> sortFragments,
        int page,
        int pageSize,
        object[]? candidateIds)
    {
        ArgumentNullException.ThrowIfNull(meta);
        var sql = new StringBuilder();
        var parameters = new List<object?>();

        var idCol = meta.Dialect.QuoteIdentifier(meta.IdColumnName);
        sql.Append("SELECT ").Append(idCol);
        sql.Append(", ").Append(meta.Dialect.CountOverWindow()).AppendLine(" AS total_count");
        sql.Append("FROM ").AppendLine(meta.QuotedTable);

        var where = new List<string>();
        foreach (var f in filterFragments)
        {
            where.Add(f.Sql);
            parameters.AddRange(f.Parameters);
        }
        if (candidateIds is not null) where.Add($"{idCol} = {meta.Dialect.ArrayParameter("@candidate_ids")}");
        if (where.Count > 0) sql.Append("WHERE ").AppendLine(string.Join(" AND ", where));

        var order = sortFragments.Select(s => s.Sql).Append($"{idCol} ASC").ToList();
        sql.Append("ORDER BY ").AppendLine(string.Join(", ", order));
        sql.Append(meta.Dialect.PagingClause(pageSize, page * pageSize));
        return new SqlFragment(sql.ToString(), parameters);
    }

    public static IReadOnlyList<SortClause> EnsureTiebreaker(EntityMetadata meta, IReadOnlyList<SortClause> sort)
    {
        ArgumentNullException.ThrowIfNull(meta);
        ArgumentNullException.ThrowIfNull(sort);

        var leadingDirection = sort.Count > 0 ? sort[^1].Direction : SortDirection.Ascending;
        var result = new List<SortClause>(sort);
        var present = new HashSet<string>(result.Select(s => s.Field), StringComparer.Ordinal);
        foreach (var part in meta.Key)
        {
            if (!present.Add(part.PropertyName)) continue;
            result.Add(new SortClause { Field = part.PropertyName, Direction = leadingDirection });
        }
        return result.Count == sort.Count ? sort : result;
    }

    public static SqlFragment BuildCursorPredicate(
        EntityMetadata meta,
        IReadOnlyList<SortClause> sortWithTiebreaker,
        CursorPayload cursor,
        CursorDirection direction,
        int parameterIndex)
    {
        ArgumentNullException.ThrowIfNull(meta);
        ArgumentNullException.ThrowIfNull(sortWithTiebreaker);
        ArgumentNullException.ThrowIfNull(cursor);

        if (direction == CursorDirection.None)
            throw new InvalidOperationException("Cursor predicate requires Forward or Backward direction.");
        if (sortWithTiebreaker.Count == 0)
            throw new InvalidOperationException("Cursor predicate requires at least one sort column (a PK tiebreaker is added by EnsureTiebreaker).");

        var totalValues = sortWithTiebreaker.Count;
        if (cursor.SortKeys.Count + meta.Key.Count != totalValues)
            throw new InvalidOperationException(
                $"Cursor payload sort-key count ({cursor.SortKeys.Count}) plus PK part count ({meta.Key.Count}) does not match the active sort column count ({totalValues}).");
        if (cursor.PrimaryKeys.Count != meta.Key.Count)
            throw new InvalidOperationException(
                $"Cursor payload primary-key count ({cursor.PrimaryKeys.Count}) does not match the key part count ({meta.Key.Count}).");

        var sb = new StringBuilder();
        var parameters = new List<object?>();
        sb.Append('(');
        for (int i = 0; i < sortWithTiebreaker.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            var col = meta.ColumnByPropertyName[sortWithTiebreaker[i].Field];
            sb.Append(meta.Dialect.QuoteIdentifier(col));
        }
        sb.Append(") ");
        sb.Append(direction == CursorDirection.Forward ? '>' : '<');
        sb.Append(" (");
        for (int i = 0; i < sortWithTiebreaker.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append('@').Append('p').Append(parameterIndex + i);
            string raw;
            Type clr;
            if (i < cursor.SortKeys.Count)
            {
                raw = cursor.SortKeys[i];
                clr = meta.ClrTypeByPropertyName[sortWithTiebreaker[i].Field];
            }
            else
            {
                var keyIndex = i - cursor.SortKeys.Count;
                raw = cursor.PrimaryKeys[keyIndex];
                clr = meta.Key[keyIndex].ClrType;
            }
            parameters.Add(ConvertFilterValue(raw, clr));
        }
        sb.Append(')');

        return new SqlFragment(sb.ToString(), parameters, parameterIndex);
    }

    private static object ConvertFilterValue(string raw, Type clrType)
    {
        var u = Nullable.GetUnderlyingType(clrType) ?? clrType;
        if (u == typeof(string)) return raw;
        if (u == typeof(Guid)) return Guid.Parse(raw);
        if (u == typeof(int)) return int.Parse(raw, CultureInfo.InvariantCulture);
        if (u == typeof(long)) return long.Parse(raw, CultureInfo.InvariantCulture);
        if (u == typeof(decimal)) return decimal.Parse(raw, CultureInfo.InvariantCulture);
        if (u == typeof(double)) return double.Parse(raw, CultureInfo.InvariantCulture);
        if (u == typeof(bool)) return bool.Parse(raw);
        if (u == typeof(DateTime)) return DateTime.Parse(raw, CultureInfo.InvariantCulture);
        if (u.IsEnum)
        {
            return Enum.TryParse(u, raw, ignoreCase: true, out var v)
                ? v
                : throw new ArgumentException($"Cannot parse '{raw}' as {u.Name}.");
        }
        return Convert.ChangeType(raw, u, CultureInfo.InvariantCulture)
               ?? throw new ArgumentException($"Cannot convert '{raw}' to {u.Name}.");
    }
}
