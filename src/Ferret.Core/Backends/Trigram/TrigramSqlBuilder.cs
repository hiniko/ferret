using System.Globalization;
using System.Text;
using Ferret.Core.Backends.Hybrid;

namespace Ferret.Core.Backends.Trigram;

internal sealed class TrigramSqlBuilder
{
    private readonly ISqlDialect _dialect;
    private readonly TrigramOptions _options;

    public TrigramSqlBuilder(ISqlDialect dialect, TrigramOptions options)
    {
        _dialect = dialect;
        _options = options;
    }

    public SearchSqlFragment BuildRanking(SearchContext ctx, int page, int pageSize)
    {
        var composite = ctx.KeyColumns is { Count: > 1 };
        var keyColsList = composite
            ? ctx.KeyColumns!.Select(_dialect.QuoteIdentifier).ToList()
            : new List<string> { _dialect.QuoteIdentifier(ctx.IdColumn) };
        var keyProjection = string.Join(", ", keyColsList);
        var srKeyProjection = string.Join(", ", keyColsList.Select(c => $"sr.{c}"));

        var props = ctx.Properties.Where(p => p.Backend == SearchBackend.Trigram).ToList();
        if (props.Count == 0)
        {
            return new SearchSqlFragment(
                $"SELECT {keyProjection}, 0.0 AS distance, 0 AS total_count " +
                $"FROM {ctx.QuotedTable} WHERE 1 = 0",
                []);
        }

        var sql = new StringBuilder();
        var parameters = new List<KeyValuePair<string, object?>>();
        var idCol = _dialect.QuoteIdentifier(ctx.IdColumn);
        var maxDistance = _options.MaxDistance.ToString("F2", CultureInfo.InvariantCulture);

        // Bug fix #2/#3: candidate IDs flow via parameter; no whole-table ID materialisation when null.
        if (ctx.HasCandidateIds)
        {
            sql.AppendLine($"WITH candidates AS ({BuildCandidatesSelect(ctx, idCol)}),");
            sql.AppendLine("field_matches AS (");
        }
        else
        {
            sql.AppendLine("WITH field_matches AS (");
        }

        var firstUnion = true;
        for (var i = 0; i < props.Count; i++)
        {
            if (!firstUnion) sql.AppendLine("  UNION ALL");
            firstUnion = false;
            EmitPropertyQuery(sql, ctx, props[i], i, keyProjection);
            parameters.Add(new KeyValuePair<string, object?>($"@p{i}", ctx.SearchTerm));
        }

        sql.AppendLine("),");
        sql.AppendLine("search_results AS (");
        sql.AppendLine($"  SELECT {keyProjection}, MIN(distance) AS distance FROM field_matches GROUP BY {keyProjection})");
        sql.AppendLine($"SELECT {srKeyProjection}, COUNT(*) OVER() AS total_count FROM search_results sr");
        sql.AppendLine($"WHERE sr.distance <= {maxDistance}");
        // Key tie-break: equal distances must order deterministically or offset pages can
        // duplicate/drop rows across requests as the plan changes.
        sql.AppendLine($"ORDER BY sr.distance, {srKeyProjection}");
        sql.Append(_dialect.PagingClause(pageSize, page * pageSize));

        return new SearchSqlFragment(sql.ToString(), parameters);
    }

    public SearchSqlFragment BuildRankedCandidate(RankedCandidateRequest req, SearchContext ctx)
    {
        var composite = ctx.KeyColumns is { Count: > 1 };
        var keyColsList = composite
            ? ctx.KeyColumns!.Select(_dialect.QuoteIdentifier).ToList()
            : new List<string> { _dialect.QuoteIdentifier(ctx.IdColumn) };
        var keyProjection = string.Join(", ", keyColsList);
        var srKeyProjection = string.Join(", ", keyColsList.Select(c => $"sr.{c}"));

        var props = ctx.Properties.Where(p => p.Backend == SearchBackend.Trigram).ToList();
        if (props.Count == 0)
        {
            return new SearchSqlFragment(
                $"SELECT {keyProjection}, row_number() OVER (ORDER BY 1) AS rnk " +
                $"FROM {ctx.QuotedTable} WHERE 1 = 0",
                []);
        }

        var maxDistance = req.ConfidenceThreshold?.ToString(CultureInfo.InvariantCulture)
            ?? _options.MaxDistance.ToString("F2", CultureInfo.InvariantCulture);

        var sql = new StringBuilder();
        var parameters = new List<KeyValuePair<string, object?>>();

        sql.AppendLine($"SELECT {keyProjection}, row_number() OVER (ORDER BY distance ASC, {keyProjection}) AS rnk");
        sql.AppendLine("FROM (");
        sql.AppendLine($"  SELECT {srKeyProjection}, sr.distance FROM (");
        sql.AppendLine($"    SELECT {keyProjection}, MIN(distance) AS distance FROM (");

        var firstUnion = true;
        for (var i = 0; i < props.Count; i++)
        {
            if (!firstUnion) sql.AppendLine("      UNION ALL");
            firstUnion = false;
            EmitPropertyQuery(sql, ctx, props[i], i, keyProjection);
            parameters.Add(new KeyValuePair<string, object?>($"@p{i}", ctx.SearchTerm));
        }

        sql.AppendLine($"    ) field_matches GROUP BY {keyProjection}");
        sql.AppendLine("  ) sr");
        sql.AppendLine($"  WHERE sr.distance <= {maxDistance}");
        sql.AppendLine($"  ORDER BY sr.distance, {srKeyProjection}");
        sql.AppendLine($"  LIMIT {req.Depth}");
        sql.Append(") ranked");

        return new SearchSqlFragment(sql.ToString(), parameters);
    }

    private void EmitPropertyQuery(StringBuilder sql, SearchContext ctx, SearchablePropertyInfo prop, int paramIdx, string keyProjection)
    {
        var paramName = $"@p{paramIdx}";

        var colName = prop.ColumnName;
        var idCol = _dialect.QuoteIdentifier(ctx.IdColumn);
        var eKeyProjection = string.Join(", ", KeyColumnsQuoted(ctx).Select(c => $"e.{c}"));

        if (prop.JoinPath.IsDirect)
        {
            // Qualify with the root alias: an unqualified column is ambiguous once the
            // candidates CTE (which exposes the key column) is joined in.
            var col = _dialect.QuoteIdentifier(colName);
            sql.AppendLine($"  SELECT {eKeyProjection}, {paramName} <<-> (e.{col})::text AS distance");
            sql.AppendLine($"  FROM {ctx.QuotedTable} e");
            if (ctx.HasCandidateIds)
            {
                sql.AppendLine($"  INNER JOIN candidates cnd ON {BuildCandidateJoin(ctx, idCol)}");
            }
            return;
        }

        // Recursive multi-hop: emit a chain of INNER JOINs. Joined groups require a
        // single owner key (composite owner keys are rejected at model build), so the
        // owner key here is always the single id column.
        var hops = prop.JoinPath.Hops;
        var aliases = hops.Select((h, idx) => h.TableAlias).ToArray();
        var leafAlias = aliases[^1];
        var leafCol = _dialect.QuoteIdentifier(colName);

        sql.AppendLine($"  SELECT e.{idCol}, MIN({paramName} <<-> ({leafAlias}.{leafCol})::text) AS distance");
        sql.AppendLine($"  FROM {ctx.QuotedTable} e");
        if (ctx.HasCandidateIds)
        {
            sql.AppendLine($"  INNER JOIN candidates cnd ON cnd.{idCol} = e.{idCol}");
        }

        var prevAlias = "e";
        var prevKeyCol = idCol;
        var hopConditions = new List<string>();
        for (var i = 0; i < hops.Count; i++)
        {
            var h = hops[i];
            var hopTbl = _dialect.QuoteIdentifier(h.TableName);
            var fkCol = _dialect.QuoteIdentifier(h.ForeignKeyColumn);
            var refKey = _dialect.QuoteIdentifier(h.ReferencedKeyColumn);

            // 1:N — child's FK points at the previous hop's key.
            // N:1 — the previous hop owns the FK; join into the referenced table's key.
            var onClause = h.ForeignKeyOwningSide
                ? $"{prevAlias}.{fkCol} = {aliases[i]}.{refKey}"
                : $"{aliases[i]}.{fkCol} = {prevAlias}.{prevKeyCol}";
            sql.AppendLine($"  INNER JOIN {hopTbl} {aliases[i]} ON {onClause}");

            if (!string.IsNullOrWhiteSpace(h.Where))
                hopConditions.Add($"({h.Where.Replace("{c}", aliases[i])})");

            prevAlias = aliases[i];
            prevKeyCol = refKey;
        }

        if (hopConditions.Count > 0)
            sql.AppendLine($"  WHERE {string.Join(" AND ", hopConditions)}");

        sql.AppendLine($"  GROUP BY e.{idCol}");
    }

    private IReadOnlyList<string> KeyColumnsQuoted(SearchContext ctx) =>
        ctx.KeyColumns is { Count: > 1 }
            ? ctx.KeyColumns.Select(_dialect.QuoteIdentifier).ToList()
            : new List<string> { _dialect.QuoteIdentifier(ctx.IdColumn) };

    private bool IsComposite(SearchContext ctx) =>
        ctx.KeyColumns is { Count: > 1 } && ctx.CandidateKeyParameterNames is { Count: > 1 };

    private string BuildCandidatesSelect(SearchContext ctx, string idCol)
    {
        if (!IsComposite(ctx))
        {
            return $"SELECT unnest({ctx.CandidateIdsParameterName}) AS {idCol}";
        }

        var keyCols = ctx.KeyColumns!;
        var paramNames = ctx.CandidateKeyParameterNames!;
        var sb = new StringBuilder("SELECT ");
        for (var i = 0; i < keyCols.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append("u").Append(i).Append(".k").Append(i)
              .Append(" AS ").Append(_dialect.QuoteIdentifier(keyCols[i]));
        }
        sb.Append(" FROM ");
        for (var i = 0; i < keyCols.Count; i++)
        {
            if (i == 0)
            {
                sb.Append("unnest(").Append(paramNames[i]).Append(") WITH ORDINALITY AS u0(k0, ord)");
            }
            else
            {
                sb.Append(" JOIN unnest(").Append(paramNames[i]).Append(") WITH ORDINALITY AS u").Append(i)
                  .Append("(k").Append(i).Append(", ord) USING (ord)");
            }
        }
        return sb.ToString();
    }

    private string BuildCandidateJoin(SearchContext ctx, string idCol)
    {
        if (!IsComposite(ctx))
        {
            return $"cnd.{idCol} = e.{idCol}";
        }

        return string.Join(" AND ", ctx.KeyColumns!.Select(c =>
        {
            var q = _dialect.QuoteIdentifier(c);
            return $"cnd.{q} = e.{q}";
        }));
    }
}
