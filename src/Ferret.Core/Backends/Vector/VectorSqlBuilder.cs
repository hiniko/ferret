using System.Globalization;
using Ferret.Abstractions.Search;
using Ferret.Abstractions.Sql;
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
        var keyList = string.Join(", ", keyColumns.Select(k => $"s.{_dialect.QuoteIdentifier(k)}"));

        var where = $"s.{col} IS NOT NULL";
        if (ctx.CandidateIdsParameterName is not null)
            where += $" AND s.{_dialect.QuoteIdentifier(ctx.IdColumn)} = ANY({ctx.CandidateIdsParameterName})";

        var innerKeyList = string.Join(", ", keyColumns.Select(k => $"s.{_dialect.QuoteIdentifier(k)}"));
        var outerKeyList = string.Join(", ", keyColumns.Select(k => $"knn.{_dialect.QuoteIdentifier(k)}"));

        // The ORDER BY ... <=> ... LIMIT must run in an inner subquery so Postgres uses the
        // HNSW index scan. Wrapping the COUNT(*) OVER() window directly around the distance
        // ORDER BY forces a WindowAgg over a full Seq Scan + Sort, bypassing the index entirely
        // (and rendering hnsw.ef_search a no-op). Count over the already-bounded KNN result.
        var sql = $"""
            SELECT {outerKeyList}, COUNT(*) OVER() AS total_count
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
