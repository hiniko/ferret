using System.Globalization;
using System.Text;
using Ferret.Core.Backends.Hybrid;

namespace Ferret.Core.Backends.FullText;

internal sealed record FullTextSqlContext
{
    public required string SourceTable { get; init; }
    public required string IdColumn { get; init; }
    public required string SearchTerm { get; init; }
    public required IReadOnlyList<FullTextGroup> Groups { get; init; }
    public required int Limit { get; init; }
    public required int Offset { get; init; }
    public string? CandidateIdsParameterName { get; init; }
    public IReadOnlyList<string>? KeyColumns { get; init; }
    public IReadOnlyList<string>? CandidateKeyParameterNames { get; init; }
    public string? SidecarSchema { get; init; }

    /// <summary>
    /// Parameterised SQL predicate (aliased against the base table as <c>e</c>) compiled from
    /// <see cref="Ferret.Abstractions.Models.PagedQuery{T,TKey}.Filter"/>. When set, the
    /// candidates_ids CTE selects directly from the base table with this predicate instead of
    /// (or, combined with <see cref="CandidateIdsParameterName"/>, in addition to) unnesting a
    /// materialised key array.
    /// </summary>
    public string? CandidatePredicateSql { get; init; }
}

internal sealed class FullTextSqlBuilder
{
    private readonly ISqlDialect _dialect;
    private readonly FullTextOptions _options;

    public FullTextSqlBuilder(ISqlDialect dialect, FullTextOptions options)
    {
        _dialect = dialect;
        _options = options;
    }

    public SearchSqlFragment BuildRanking(FullTextSqlContext ctx)
    {
        if (ctx.Groups.Count == 0)
        {
            var emptyKeyCols = ctx.KeyColumns is { Count: > 1 } ? ctx.KeyColumns : new[] { ctx.IdColumn };
            var emptyProjection = string.Join(", ", emptyKeyCols.Select(c => _dialect.QuoteIdentifier(c)));
            return new SearchSqlFragment(
                $"SELECT {emptyProjection}, 0 AS total_count FROM {_dialect.QuoteIdentifier(ctx.SourceTable)} WHERE 1 = 0",
                []);
        }

        var sidecar = FullTextSidecarNaming.TableName(ctx.SourceTable, _options);
        var sidecarQ = ctx.SidecarSchema is null
            ? _dialect.QuoteIdentifier(sidecar)
            : $"{_dialect.QuoteIdentifier(ctx.SidecarSchema)}.{_dialect.QuoteIdentifier(sidecar)}";
        var idQuoted = _dialect.QuoteIdentifier(ctx.IdColumn);

        var parserFn = _options.DefaultParser switch
        {
            FullTextParser.Plain  => "plainto_tsquery",
            FullTextParser.Phrase => "phraseto_tsquery",
            FullTextParser.Raw    => "to_tsquery",
            _                     => "websearch_to_tsquery",
        };

        var configs = ctx.Groups.Select(g => g.FullTextConfig).Distinct().ToList();
        var configAliases = configs
            .Select((c, i) => (Config: c, Alias: $"q_{i}"))
            .ToDictionary(t => t.Config, t => t.Alias);

        var hasCandidateIds = !string.IsNullOrEmpty(ctx.CandidateIdsParameterName);
        var hasPredicate = ctx.CandidatePredicateSql is not null;
        var hasCandidates = hasCandidateIds || hasPredicate;
        var composite = ctx.KeyColumns is { Count: > 1 };
        var compositeCandidateArray = composite
            && hasCandidateIds
            && ctx.CandidateKeyParameterNames is { Count: > 1 };
        // The candidates_ids CTE always projects the real (N-column) key when the entity is
        // composite and either a predicate or a composite key array feeds it — the join must
        // match that shape regardless of which source populated it.
        var candidatesIdsIsComposite = composite && (hasPredicate || compositeCandidateArray);

        // Output key projection: single column → "id"; composite → the N real key columns.
        var keyCols = composite ? ctx.KeyColumns! : new[] { ctx.IdColumn };
        var innerProjection = composite
            ? string.Join(", ", keyCols.Select(c => $"s.{_dialect.QuoteIdentifier(c)}"))
            : $"s.{idQuoted} AS id";
        var outerProjection = composite
            ? string.Join(", ", keyCols.Select(c => _dialect.QuoteIdentifier(c)))
            : "id";

        var sb = new StringBuilder();
        if (hasCandidates)
        {
            sb.Append("WITH candidates_ids AS (")
              .Append(BuildCandidatesSelect(ctx, composite, compositeCandidateArray, hasCandidateIds))
              .AppendLine("),");
            sb.AppendLine("candidates AS (");
        }
        else
        {
            sb.AppendLine("WITH candidates AS (");
        }
        sb.Append("    SELECT ").Append(innerProjection).Append(", ");

        EmitRankExpr(sb, ctx, configAliases);
        sb.AppendLine(" AS rank");

        sb.Append("    FROM ").Append(sidecarQ).AppendLine(" s");
        if (hasCandidates)
        {
            sb.Append("    INNER JOIN candidates_ids c ON ").Append(BuildCandidateJoin(ctx, candidatesIdsIsComposite, idQuoted)).AppendLine();
        }
        foreach (var (cfg, alias) in configAliases)
        {
            sb.Append("    CROSS JOIN (SELECT ").Append(parserFn).Append('(')
              .Append(QuoteLiteral(cfg))
              .Append(", @q) AS q) ").AppendLine(alias);
        }

        sb.Append("    WHERE ");
        for (var i = 0; i < ctx.Groups.Count; i++)
        {
            if (i > 0) sb.Append(" OR ");
            var col = _dialect.QuoteIdentifier(FullTextSidecarNaming.ColumnName(ctx.Groups[i].Name, _options));
            var alias = configAliases[ctx.Groups[i].FullTextConfig];
            sb.Append("s.").Append(col).Append(" @@ ").Append(alias).Append(".q");
        }
        sb.AppendLine();
        sb.AppendLine(")");

        sb.Append("SELECT ").Append(outerProjection).Append(", COUNT(*) OVER() AS total_count FROM candidates ORDER BY rank DESC, ").Append(outerProjection).Append(' ');
        sb.Append(_dialect.PagingClause(ctx.Limit, ctx.Offset));

        var parameters = new List<KeyValuePair<string, object?>>
        {
            new("@q", ctx.SearchTerm),
        };
        return new SearchSqlFragment(sb.ToString(), parameters);
    }

    public SearchSqlFragment BuildRankedCandidate(RankedCandidateRequest req, FullTextSqlContext ctx)
    {
        var composite = ctx.KeyColumns is { Count: > 1 };
        var keyCols = composite ? ctx.KeyColumns! : new[] { ctx.IdColumn };

        if (ctx.Groups.Count == 0)
        {
            var emptyProjection = string.Join(", ", keyCols.Select(c => _dialect.QuoteIdentifier(c)));
            return new SearchSqlFragment(
                $"SELECT {emptyProjection}, row_number() OVER (ORDER BY 1) AS rnk " +
                $"FROM {_dialect.QuoteIdentifier(ctx.SourceTable)} WHERE 1 = 0",
                []);
        }

        var sidecar = FullTextSidecarNaming.TableName(ctx.SourceTable, _options);
        var sidecarQ = ctx.SidecarSchema is null
            ? _dialect.QuoteIdentifier(sidecar)
            : $"{_dialect.QuoteIdentifier(ctx.SidecarSchema)}.{_dialect.QuoteIdentifier(sidecar)}";
        var idQuoted = _dialect.QuoteIdentifier(ctx.IdColumn);

        var parserFn = _options.DefaultParser switch
        {
            FullTextParser.Plain  => "plainto_tsquery",
            FullTextParser.Phrase => "phraseto_tsquery",
            FullTextParser.Raw    => "to_tsquery",
            _                     => "websearch_to_tsquery",
        };

        var configs = ctx.Groups.Select(g => g.FullTextConfig).Distinct().ToList();
        var configAliases = configs
            .Select((c, i) => (Config: c, Alias: $"q_{i}"))
            .ToDictionary(t => t.Config, t => t.Alias);

        var innerProjection = composite
            ? string.Join(", ", keyCols.Select(c => $"s.{_dialect.QuoteIdentifier(c)}"))
            : $"s.{idQuoted} AS id";
        var candProjection = composite
            ? string.Join(", ", keyCols.Select(c => _dialect.QuoteIdentifier(c)))
            : "id";

        var sb = new StringBuilder();
        sb.Append("SELECT ").Append(candProjection).Append(", row_number() OVER (ORDER BY rank DESC, ").Append(candProjection).AppendLine(") AS rnk");
        sb.AppendLine("FROM (");
        sb.Append("    SELECT ").Append(innerProjection).Append(", ");
        EmitRankExpr(sb, ctx, configAliases);
        sb.AppendLine(" AS rank");
        sb.Append("    FROM ").Append(sidecarQ).AppendLine(" s");
        foreach (var (cfg, alias) in configAliases)
        {
            sb.Append("    CROSS JOIN (SELECT ").Append(parserFn).Append('(')
              .Append(QuoteLiteral(cfg))
              .Append(", @q) AS q) ").AppendLine(alias);
        }
        sb.Append("    WHERE ");
        for (var i = 0; i < ctx.Groups.Count; i++)
        {
            if (i > 0) sb.Append(" OR ");
            var col = _dialect.QuoteIdentifier(FullTextSidecarNaming.ColumnName(ctx.Groups[i].Name, _options));
            var alias = configAliases[ctx.Groups[i].FullTextConfig];
            sb.Append("s.").Append(col).Append(" @@ ").Append(alias).Append(".q");
        }
        sb.AppendLine();
        sb.AppendLine(") candidates");
        if (req.ConfidenceThreshold is { } threshold)
            sb.Append("WHERE rank >= ").Append(threshold.ToString(CultureInfo.InvariantCulture)).AppendLine();
        sb.Append("ORDER BY rank DESC, ").Append(candProjection).Append(" LIMIT ").Append(req.Depth);

        var parameters = new List<KeyValuePair<string, object?>>
        {
            new("@q", ctx.SearchTerm),
        };
        return new SearchSqlFragment(sb.ToString(), parameters);
    }

    private void EmitRankExpr(StringBuilder sb, FullTextSqlContext ctx, Dictionary<string, string> configAliases)
    {
        var terms = ctx.Groups.Select(g =>
        {
            var col = _dialect.QuoteIdentifier(FullTextSidecarNaming.ColumnName(g.Name, _options));
            var alias = configAliases[g.FullTextConfig];
            return $"ts_rank_cd(s.{col}, {alias}.q)";
        }).ToList();

        if (_options.GroupCombinator == GroupCombinator.Sum)
        {
            sb.Append('(').Append(string.Join(" + ", terms)).Append(')');
        }
        else
        {
            sb.Append("GREATEST(").Append(string.Join(", ", terms)).Append(')');
        }
    }

    private string BuildCandidatesSelect(
        FullTextSqlContext ctx, bool entityComposite, bool arrayComposite, bool hasCandidateIds)
    {
        // Filter pushdown: a predicate selects straight from the base table instead of
        // (or, combined with a key array, in addition to) unnesting a materialised array.
        // Uses entityComposite (the real key shape), not arrayComposite (whether a *key array*
        // happens to be present) — a composite entity's predicate select always needs every
        // key column projected, even when there is no candidate array at all.
        if (ctx.CandidatePredicateSql is { } predicate)
        {
            var keyCols = entityComposite ? ctx.KeyColumns! : new[] { ctx.IdColumn };
            var keyProjection = string.Join(", ", keyCols.Select(c => $"e.{_dialect.QuoteIdentifier(c)}"));
            var where = predicate;
            if (hasCandidateIds)
                where += arrayComposite
                    ? $" AND {BuildCompositeKeyMembership(ctx, "e")}"
                    : $" AND e.{_dialect.QuoteIdentifier(ctx.IdColumn)} = ANY({ctx.CandidateIdsParameterName})";
            return $"SELECT {keyProjection} FROM {_dialect.QuoteIdentifier(ctx.SourceTable)} e WHERE {where}";
        }

        if (!arrayComposite)
        {
            return $"SELECT unnest({ctx.CandidateIdsParameterName}) AS id";
        }

        var candKeyCols = ctx.KeyColumns!;
        var paramNames = ctx.CandidateKeyParameterNames!;
        var sb = new StringBuilder("SELECT ");
        for (var i = 0; i < candKeyCols.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append("u").Append(i).Append(".k").Append(i)
              .Append(" AS ").Append(_dialect.QuoteIdentifier(candKeyCols[i]));
        }
        sb.Append(" FROM ");
        for (var i = 0; i < candKeyCols.Count; i++)
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

    /// <summary>
    /// Composite-key membership test against the candidate key arrays, evaluated row-wise via
    /// <c>WITH ORDINALITY</c>-zipped unnest, expressed as an <c>IN</c> predicate so it can be
    /// ANDed onto a base-table filter select.
    /// </summary>
    private string BuildCompositeKeyMembership(FullTextSqlContext ctx, string alias)
    {
        var keyCols = ctx.KeyColumns!;
        var paramNames = ctx.CandidateKeyParameterNames!;
        var lhs = string.Join(", ", keyCols.Select(c => $"{alias}.{_dialect.QuoteIdentifier(c)}"));
        var sb = new StringBuilder("(").Append(lhs).Append(") IN (SELECT ");
        for (var i = 0; i < keyCols.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append("u").Append(i).Append(".k").Append(i);
        }
        sb.Append(" FROM ");
        for (var i = 0; i < keyCols.Count; i++)
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

    private string BuildCandidateJoin(FullTextSqlContext ctx, bool composite, string idQuoted)
    {
        if (!composite)
        {
            return $"c.id = s.{idQuoted}";
        }

        return string.Join(" AND ", ctx.KeyColumns!.Select(col =>
        {
            var q = _dialect.QuoteIdentifier(col);
            return $"c.{q} = s.{q}";
        }));
    }

    private static string QuoteLiteral(string s) => "'" + s.Replace("'", "''") + "'";
}
