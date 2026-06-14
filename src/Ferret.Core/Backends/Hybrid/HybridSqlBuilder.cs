using System.Globalization;
using System.Text;
using Ferret.Abstractions.Sql;

namespace Ferret.Core.Backends.Hybrid;

internal sealed record HybridSqlContext
{
    public required IReadOnlyList<string> KeyColumns { get; init; }
    public required int Limit { get; init; }
    public required int Offset { get; init; }
    public required int RrfK { get; init; }
    public required IReadOnlyList<HybridBackendFragment> Backends { get; init; }
}

internal sealed record HybridBackendFragment
{
    public required string CteName { get; init; }
    public required SearchSqlFragment Body { get; init; }
    public required double Weight { get; init; }
}

internal sealed class HybridSqlBuilder
{
    private readonly ISqlDialect _dialect;

    public HybridSqlBuilder(ISqlDialect dialect) => _dialect = dialect;

    public SearchSqlFragment Build(HybridSqlContext ctx)
    {
        var keyCols = string.Join(", ", ctx.KeyColumns.Select(_dialect.QuoteIdentifier));

        var sb = new StringBuilder();
        sb.Append("WITH ");
        for (var i = 0; i < ctx.Backends.Count; i++)
        {
            var backend = ctx.Backends[i];
            sb.Append(backend.CteName).Append(" AS (").Append(backend.Body.Sql).Append(')');
            sb.Append(',').Append('\n');
        }

        sb.Append("fused AS (\n");
        sb.Append("  SELECT ").Append(keyCols).Append(",\n");
        sb.Append("         SUM(r.w * 1.0 / (").Append(ctx.RrfK).Append(" + r.rnk)) AS rrf,\n");
        sb.Append("         COUNT(*) OVER() AS total_count\n");
        sb.Append("  FROM (\n");
        for (var i = 0; i < ctx.Backends.Count; i++)
        {
            var backend = ctx.Backends[i];
            var w = backend.Weight.ToString(CultureInfo.InvariantCulture);
            sb.Append("    SELECT ").Append(keyCols).Append(", rnk, ").Append(w).Append(" AS w FROM ").Append(backend.CteName);
            if (i < ctx.Backends.Count - 1)
                sb.Append("\n    UNION ALL");
            sb.Append('\n');
        }
        sb.Append("  ) r\n");
        sb.Append("  GROUP BY ").Append(keyCols).Append('\n');
        sb.Append(")\n");
        sb.Append("SELECT ").Append(keyCols).Append(", total_count\n");
        sb.Append("FROM fused\n");
        sb.Append("ORDER BY rrf DESC, ").Append(keyCols).Append('\n');
        sb.Append("LIMIT ").Append(ctx.Limit).Append(" OFFSET ").Append(ctx.Offset);

        var parameters = ctx.Backends
            .SelectMany(b => b.Body.Parameters)
            .ToList();

        return new SearchSqlFragment(sb.ToString(), parameters);
    }
}
