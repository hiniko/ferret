using System.Globalization;
using System.Text;
using Ferret.Core.Backends.Hybrid;

namespace Ferret.Core.Backends.Vector;

internal sealed record VectorSqlContext
{
    public required string SidecarTable { get; init; }
    public string? SidecarSchema { get; init; }
    public required string GroupColumn { get; init; }
    public required string IdColumn { get; init; }
    public IReadOnlyList<string>? KeyColumns { get; init; }
    public required int Limit { get; init; }
    public required int Offset { get; init; }
    public required string QueryVectorParameterName { get; init; }
    public required int EfSearch { get; init; }
    public string? CandidateIdsParameterName { get; init; }
    public IReadOnlyList<string>? CandidateKeyParameterNames { get; init; }

    /// <summary>
    /// Parameterised SQL predicate (aliased against the base table as <c>e</c>) compiled from
    /// <see cref="Ferret.Abstractions.Models.PagedQuery{T,TKey}.Filter"/>. Filterable columns
    /// live on the base table, not the vector sidecar, so when set the ranking query joins the
    /// sidecar to a <c>candidates_ids</c> CTE selecting from the base table with this predicate
    /// instead of (or, combined with a key array, in addition to) restricting the sidecar
    /// directly via <c>= ANY(@candidate_ids)</c>.
    /// </summary>
    public string? CandidatePredicateSql { get; init; }

    /// <summary>Base table name (quoted by the caller is not required — this builder quotes it).</summary>
    public string? BaseTable { get; init; }
}

internal sealed class VectorSqlBuilder
{
    private readonly ISqlDialect _dialect;
    private readonly VectorOptions _options;

    public VectorSqlBuilder(ISqlDialect dialect, VectorOptions options)
    {
        _dialect = dialect;
        _options = options;
    }

    public SearchSqlFragment BuildRanking(VectorSqlContext ctx)
    {
        var schemaPrefix = string.IsNullOrEmpty(ctx.SidecarSchema)
            ? string.Empty
            : $"{_dialect.QuoteIdentifier(ctx.SidecarSchema)}.";
        var sidecar = $"{schemaPrefix}{_dialect.QuoteIdentifier(ctx.SidecarTable)}";
        var col = _dialect.QuoteIdentifier(ctx.GroupColumn);
        var keyColumns = ctx.KeyColumns ?? new[] { ctx.IdColumn };
        var composite = keyColumns.Count > 1;

        var where = $"s.{col} IS NOT NULL";
        var preSql = "";
        if (ctx.CandidatePredicateSql is { } predicate)
        {
            // Filterable columns live on the base table, not the sidecar: join the sidecar to
            // a candidates_ids CTE selecting from the base table with the predicate, instead of
            // (or, combined with a key array, in addition to) restricting the sidecar directly.
            var keyProjection = string.Join(", ", keyColumns.Select(c => $"e.{_dialect.QuoteIdentifier(c)}"));
            var predicateWhere = predicate;
            if (ctx.CandidateIdsParameterName is not null)
                predicateWhere += composite
                    ? $" AND {BuildCompositeKeyMembership(ctx, keyColumns, "e")}"
                    : $" AND e.{_dialect.QuoteIdentifier(ctx.IdColumn)} = ANY({ctx.CandidateIdsParameterName})";
            var baseTable = _dialect.QuoteIdentifier(ctx.BaseTable!);
            preSql = $"WITH candidates_ids AS (SELECT {keyProjection} FROM {baseTable} e WHERE {predicateWhere})\n";
            var joinCond = string.Join(" AND ", keyColumns.Select(k =>
            {
                var q = _dialect.QuoteIdentifier(k);
                return $"c.{q} = s.{q}";
            }));
            where += $" AND EXISTS (SELECT 1 FROM candidates_ids c WHERE {joinCond})";
        }
        else if (ctx.CandidateIdsParameterName is not null)
        {
            where += $" AND s.{_dialect.QuoteIdentifier(ctx.IdColumn)} = ANY({ctx.CandidateIdsParameterName})";
        }

        var innerKeyList = string.Join(", ", keyColumns.Select(k => $"s.{_dialect.QuoteIdentifier(k)}"));
        var outerKeyList = string.Join(", ", keyColumns.Select(k => $"knn.{_dialect.QuoteIdentifier(k)}"));

        // The ORDER BY ... <=> ... LIMIT must run in an inner subquery so Postgres uses the
        // HNSW index scan. Wrapping the COUNT(*) OVER() window directly around the distance
        // ORDER BY forces a WindowAgg over a full Seq Scan + Sort, bypassing the index entirely
        // (and rendering hnsw.ef_search a no-op). Count over the already-bounded KNN result.
        var sql = $"""
            {preSql}SELECT {outerKeyList}, COUNT(*) OVER() AS total_count
            FROM (
                SELECT {innerKeyList}
                FROM {sidecar} s
                WHERE {where}
                ORDER BY s.{col} <=> ({ctx.QueryVectorParameterName})::vector
                LIMIT {ctx.Limit} OFFSET {ctx.Offset}
            ) knn
            """;

        return new SearchSqlFragment(sql, Array.Empty<KeyValuePair<string, object?>>());
    }

    /// <summary>
    /// Composite-key membership test against the candidate key arrays, evaluated row-wise via
    /// <c>WITH ORDINALITY</c>-zipped unnest, expressed as an <c>IN</c> predicate so it can be
    /// ANDed onto a base-table filter select.
    /// </summary>
    private string BuildCompositeKeyMembership(VectorSqlContext ctx, IReadOnlyList<string> keyColumns, string alias)
    {
        var paramNames = ctx.CandidateKeyParameterNames!;
        var lhs = string.Join(", ", keyColumns.Select(c => $"{alias}.{_dialect.QuoteIdentifier(c)}"));
        var sb = new StringBuilder("(").Append(lhs).Append(") IN (SELECT ");
        for (var i = 0; i < keyColumns.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append("u").Append(i).Append(".k").Append(i);
        }
        sb.Append(" FROM ");
        for (var i = 0; i < keyColumns.Count; i++)
        {
            if (i == 0)
                sb.Append("unnest(").Append(paramNames[i]).Append(") WITH ORDINALITY AS u0(k0, ord)");
            else
                sb.Append(" JOIN unnest(").Append(paramNames[i]).Append(") WITH ORDINALITY AS u").Append(i)
                  .Append("(k").Append(i).Append(", ord) USING (ord)");
        }
        sb.Append(')');
        return sb.ToString();
    }

    public SearchSqlFragment BuildRankedCandidate(RankedCandidateRequest req, string sidecarTable, string groupColumn)
    {
        var schemaPrefix = string.IsNullOrEmpty(req.SidecarSchema)
            ? string.Empty
            : $"{_dialect.QuoteIdentifier(req.SidecarSchema)}.";
        var sidecar = $"{schemaPrefix}{_dialect.QuoteIdentifier(sidecarTable)}";
        var col = _dialect.QuoteIdentifier(groupColumn);
        var qvec = req.QueryVectorParameterName!;
        var distanceExpr = $"s.{col} <=> ({qvec})::vector";

        var innerKeyList = string.Join(", ", req.KeyColumns.Select(k => $"s.{_dialect.QuoteIdentifier(k)}"));
        var outerKeyList = string.Join(", ", req.KeyColumns.Select(k => $"v.{_dialect.QuoteIdentifier(k)}"));

        var where = $"s.{col} IS NOT NULL";
        if (req.ConfidenceThreshold is { } threshold)
            where += $" AND {distanceExpr} <= {threshold.ToString(CultureInfo.InvariantCulture)}";

        // Inner ORDER BY <=> LIMIT is required so Postgres uses the HNSW index scan.
        var sql = $"""
            SELECT {outerKeyList}, row_number() OVER (ORDER BY v.distance, {outerKeyList}) AS rnk
            FROM (
                SELECT {innerKeyList}, {distanceExpr} AS distance
                FROM {sidecar} s
                WHERE {where}
                ORDER BY {distanceExpr}
                LIMIT {req.Depth}
            ) v
            """;

        return new SearchSqlFragment(sql, Array.Empty<KeyValuePair<string, object?>>());
    }
}
