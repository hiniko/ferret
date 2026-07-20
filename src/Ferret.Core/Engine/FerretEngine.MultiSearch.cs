using System.Diagnostics;
using Ferret.Abstractions.Attributes;
using Ferret.Abstractions.Search;
using Ferret.Core.Backends.FullText;
using Ferret.Core.Backends.Hybrid;
using Ferret.Core.Backends.Trigram;
using Ferret.Core.Diagnostics;
using Ferret.Core.Engine.MultiSearch;
using Tags = Ferret.Core.Diagnostics.FerretDiagnostics.Tags;

namespace Ferret.Core.Engine;

internal sealed partial class FerretEngine
{
    public async Task<MultiSearchResult> MultiSearchAsync(
        IFerretSession session,
        MultiSearchQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (string.IsNullOrWhiteSpace(query.Search))
        {
            throw new InvalidOperationException("Multisearch requires a non-blank search term; there is no browse mode.");
        }
        if (query.Entities.Count == 0)
        {
            throw new InvalidOperationException("Multisearch requires at least one entity registration.");
        }
        if (query.PerEntityLimit < 1)
        {
            throw new ArgumentException($"PerEntityLimit must be positive; got {query.PerEntityLimit}.");
        }

        var arms = BuildArms(session, query);

        using var activity = FerretDiagnostics.ActivitySource.StartActivity("ferret.search.multi", ActivityKind.Client);
        activity?.SetTag(Tags.Mode, "multi");
        activity?.SetTag(Tags.Limit, query.PerEntityLimit);
        activity?.SetTag(Tags.Entity, string.Join(",", arms.Select(a => a.Model.TableName)));

        var sw = Stopwatch.StartNew();
        try
        {
            var result = await ExecuteMultiSearchAsync(session, arms, ct);
            activity?.SetTag(Tags.RowCount, result.Groups.Sum(g => g.Hits.Count));
            ObserveSlowQuery(activity, sw, "(multi)", "multi");
            return result;
        }
        catch (Exception ex)
        {
            RecordFailure(activity, ex, "(multi)", "multi");
            throw;
        }
    }

    private sealed record MultiSearchArm(
        EntityModel Model,
        CompiledProjection Projection,
        string Sql,
        IReadOnlyList<KeyValuePair<string, object?>> Parameters);

    private List<MultiSearchArm> BuildArms(IFerretSession session, MultiSearchQuery query)
    {
        var definitions = query.Entities;
        var models = definitions.Select(d => _registry.Get(d.EntityType)).ToList();

        if (query.Types is { Count: > 0 })
        {
            var known = models.Select(m => m.TableName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var unknown = query.Types.Where(t => !known.Contains(t)).ToList();
            if (unknown.Count > 0)
            {
                throw new ArgumentException(
                    $"Unknown multisearch type(s): {string.Join(", ", unknown)}. Valid types: {string.Join(", ", known)}.");
            }
        }

        var term = query.Search.Trim();
        var arms = new List<MultiSearchArm>();
        for (var i = 0; i < definitions.Count; i++)
        {
            var model = models[i];
            if (query.Types is { Count: > 0 }
                && !query.Types.Contains(model.TableName, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (model.HybridConfig is not null
                || model.VectorGroups.Count > 0
                || model.SearchableProperties.Any(p => p.Backend == SearchBackend.Vector))
            {
                throw new NotSupportedException(
                    $"Entity '{model.TableName}': vector and hybrid backends are not supported in multisearch v1; trigram and fulltext only.");
            }

            arms.Add(BuildArm(session, model, definitions[i], term, query.PerEntityLimit));
        }
        return arms;
    }

    private MultiSearchArm BuildArm(
        IFerretSession session, EntityModel model, MultiSearchEntityDefinition definition,
        string term, int perEntityLimit)
    {
        var dialect = session.Dialect;
        var meta = EntityMetadata.From(model, dialect);
        var projection = ProjectionTranslator.Translate(definition.Projection, model);
        var backend = ResolveBackend(model);
        var keyColumns = model.Key.Select(k => k.ColumnName).ToList();

        var request = new RankedCandidateRequest
        {
            SourceTable = model.TableName,
            KeyColumns = keyColumns,
            SearchTerm = term,
            Depth = perEntityLimit,
            CteName = "msq",
        };

        SearchSqlFragment fragment;
        switch (backend)
        {
            case FullTextSearchBackend ft:
            {
                fragment = ft.BuildRankedCandidate(request with { SidecarSchema = ft.SidecarSchema }, new FullTextSqlContext
                {
                    SourceTable = model.TableName,
                    IdColumn = model.KeyColumnName,
                    SearchTerm = term,
                    Groups = model.FullTextGroups,
                    Limit = perEntityLimit,
                    Offset = 0,
                    SidecarSchema = ft.SidecarSchema,
                    KeyColumns = meta.IsComposite ? keyColumns : null,
                });
                break;
            }
            case TrigramSearchBackend tg:
            {
                fragment = tg.BuildRankedCandidate(request, new SearchContext
                {
                    Properties = model.SearchableProperties,
                    SearchTerm = term,
                    IdColumn = model.KeyColumnName,
                    QuotedTable = model.QuotedTable(dialect),
                    HasCandidateIds = false,
                    KeyColumns = meta.IsComposite ? keyColumns : null,
                });
                break;
            }
            default:
                throw new NotSupportedException(
                    $"Entity '{model.TableName}': backend '{backend.Name}' is not supported in multisearch v1.");
        }

        var parameters = new List<KeyValuePair<string, object?>>(fragment.Parameters);

        var filterSql = new List<string>();
        var filterParamIndex = 0;
        foreach (var filter in definition.Filters)
        {
            var frag = PagedSqlBuilder.CompileFilter(filter, meta, filterParamIndex, tableAlias: "e", parameterPrefix: "f");
            filterSql.Add(frag.Sql);
            for (var j = 0; j < frag.Parameters.Count; j++)
            {
                parameters.Add(new KeyValuePair<string, object?>($"@f{filterParamIndex + j}", frag.Parameters[j]));
            }
            filterParamIndex += frag.Parameters.Count;
        }

        var quotedKeyCols = model.Key.Select(k => dialect.QuoteIdentifier(k.ColumnName)).ToList();
        // Single-key fulltext fragments alias their key output as "id"; every other shape keeps real column names.
        IReadOnlyList<string> rankedKeyCols = meta.IsComposite
            ? quotedKeyCols
            : [backend is FullTextSearchBackend ? "id" : quotedKeyCols[0]];

        var joinConditions = string.Join(" AND ",
            quotedKeyCols.Select((col, idx) => $"e.{col} = r.{rankedKeyCols[idx]}"));
        var slotSelects = projection.ColumnNames.Count > 0
            ? string.Join(", ", projection.ColumnNames.Select(col => $"e.{dialect.QuoteIdentifier(col)}"))
            : "1";
        var where = filterSql.Count > 0 ? $"WHERE {string.Join(" AND ", filterSql)}\n" : "";

        var sql =
            $"SELECT {slotSelects}\n" +
            $"FROM (\n{fragment.Sql}\n) r\n" +
            $"JOIN {model.QuotedTable(dialect)} e ON {joinConditions}\n" +
            $"{where}ORDER BY r.rnk";

        return new MultiSearchArm(model, projection, sql, parameters);
    }

    private async Task<MultiSearchResult> ExecuteMultiSearchAsync(
        IFerretSession session, List<MultiSearchArm> arms, CancellationToken ct)
    {
        var connection = await session.OpenConnectionAsync(ct);

        await using var batch = connection.CreateBatch();
        foreach (var arm in arms)
        {
            var command = batch.CreateBatchCommand();
            command.CommandText = arm.Sql;
            foreach (var (name, value) in arm.Parameters)
            {
                command.AddParameter(name, value);
            }
            batch.BatchCommands.Add(command);
            LogStatementDebug(arm.Sql);
        }

        var groups = new List<MultiSearchGroup>(arms.Count);
        await using var reader = await batch.ExecuteReaderAsync(ct);
        for (var i = 0; i < arms.Count; i++)
        {
            var arm = arms[i];
            var slotCount = arm.Projection.ColumnNames.Count;
            var hits = new List<object>();

            while (await reader.ReadAsync(ct))
            {
                var values = new object?[slotCount];
                for (var s = 0; s < slotCount; s++)
                {
                    var raw = reader.GetValue(s);
                    values[s] = raw is DBNull ? null : raw;
                }

                hits.Add(arm.Projection.Materializer(values));
            }

            groups.Add(new MultiSearchGroup { EntityType = arm.Model.TableName, Hits = hits });
            if (i < arms.Count - 1)
            {
                await reader.NextResultAsync(ct);
            }
        }

        return new MultiSearchResult { Groups = groups };
    }
}
