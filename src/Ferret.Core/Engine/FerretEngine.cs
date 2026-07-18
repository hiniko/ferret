using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using Ferret.Abstractions.Attributes;
using Ferret.Abstractions.Search;
using Ferret.Core.Backends.FullText;
using Ferret.Core.Backends.Hybrid;
using Ferret.Core.Backends.Trigram;
using Ferret.Core.Backends.Vector;
using Ferret.Core.Engine.Reindex;
using Microsoft.Extensions.Logging;
using Npgsql;
using Ferret.Core.Diagnostics;
using Tags = Ferret.Core.Diagnostics.FerretDiagnostics.Tags;

namespace Ferret.Core.Engine;

internal sealed class FerretEngine : IFerretEngine
{
    private readonly EntityRegistry _registry;
    private readonly IReadOnlyList<ISearchBackend> _backends;
    private readonly ILogger<FerretEngine> _logger;
    private readonly FerretRuntimeOptions _options;
    private readonly HybridOptions? _hybridOptions;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(string Entity, string Group), string> _activeVectorColumns = new();

    public FerretEngine(
        EntityRegistry registry,
        IEnumerable<ISearchBackend> backends,
        ILogger<FerretEngine> logger,
        FerretRuntimeOptions options,
        HybridOptions? hybridOptions = null)
    {
        _registry = registry;
        _backends = backends.ToList();
        _logger = logger;
        _options = options;
        _hybridOptions = hybridOptions;
    }

    public async Task<OffsetResult<T>> SearchOffsetAsync<T, TKey>(
        IFerretSession session,
        PagedQuery<T, TKey> query,
        CancellationToken ct = default)
        where T : class
        where TKey : notnull
    {
        if (query.Mode != PaginationMode.Offset)
            throw new InvalidOperationException(
                $"SearchOffsetAsync invoked with PagedQuery.Mode={query.Mode}; expected {PaginationMode.Offset}.");

        var model = _registry.Get<T>();
        var meta = EntityMetadata.From(model, session.Dialect);
        var limit = query.Limit;
        var page = query.Page ?? 0;
        var hasSearch = !string.IsNullOrWhiteSpace(query.Search);

        using var activity = FerretDiagnostics.ActivitySource.StartActivity("ferret.search.offset", ActivityKind.Client);
        activity?.SetTag(Tags.Entity, meta.TableName);
        activity?.SetTag(Tags.Mode, "offset");
        activity?.SetTag(Tags.Limit, limit);
        activity?.SetTag(Tags.Page, page);
        activity?.SetTag(Tags.HasSearch, hasSearch);
        activity?.SetTag(Tags.FilterCount, query.Filter.Count);
        activity?.SetTag(Tags.SortCount, query.Sort.Count);

        var sw = Stopwatch.StartNew();
        try
        {
            var result = hasSearch
                ? await ExecuteOffsetSearchAsync<T, TKey>(session, model, meta, query, page, limit, ct)
                : await ExecuteOffsetStandardAsync<T, TKey>(session, model, meta, query, page, limit, ct);

            activity?.SetTag(Tags.RowCount, result.Items.Count);
            activity?.SetTag(Tags.TotalCount, result.TotalCount);
            ObserveSlowQuery(activity, sw, meta.TableName, "offset");
            return result;
        }
        catch (Exception ex)
        {
            RecordFailure(activity, ex, meta.TableName, "offset");
            throw;
        }
    }

    public async Task<CursorResult<T>> SearchCursorAsync<T, TKey>(
        IFerretSession session,
        PagedQuery<T, TKey> query,
        CancellationToken ct = default)
        where T : class
        where TKey : notnull
    {
        if (query.Mode != PaginationMode.Cursor)
            throw new InvalidOperationException(
                $"SearchCursorAsync invoked with PagedQuery.Mode={query.Mode}; expected {PaginationMode.Cursor}.");

        var model = _registry.Get<T>();
        var meta = EntityMetadata.From(model, session.Dialect);
        var limit = query.Limit;
        var hasSearch = !string.IsNullOrWhiteSpace(query.Search);

        using var activity = FerretDiagnostics.ActivitySource.StartActivity("ferret.search.cursor", ActivityKind.Client);
        activity?.SetTag(Tags.Entity, meta.TableName);
        activity?.SetTag(Tags.Mode, "cursor");
        activity?.SetTag(Tags.Limit, limit);
        activity?.SetTag(Tags.HasSearch, hasSearch);
        activity?.SetTag(Tags.FilterCount, query.Filter.Count);
        activity?.SetTag(Tags.SortCount, query.Sort.Count);

        var sw = Stopwatch.StartNew();
        try
        {
            var result = hasSearch
                ? await ExecuteSearchCursorAsync<T, TKey>(session, model, meta, query, limit, activity, ct)
                : await ExecuteCursorAsync<T, TKey>(session, model, meta, query, limit, activity, ct);
            activity?.SetTag(Tags.RowCount, result.Items.Count);
            ObserveSlowQuery(activity, sw, meta.TableName, "cursor");
            return result;
        }
        catch (Exception ex)
        {
            RecordFailure(activity, ex, meta.TableName, "cursor");
            throw;
        }
    }

    public async Task ReindexAsync<T>(
        IFerretSession session,
        string group,
        ReindexOptions? options = null,
        CancellationToken ct = default)
        where T : class
    {
        options ??= new ReindexOptions();
        var model = _registry.Get<T>();

        if (model.VectorGroups.Any(g => g.Name == group))
        {
            var vectorBackend = _backends.OfType<VectorSearchBackend>().FirstOrDefault()
                ?? throw new InvalidOperationException(
                    "ReindexAsync requires the vector search backend. Call UseVectorSearch(...).");
            var vectorOptions = vectorBackend.Options;
            var vgroup = model.VectorGroups.First(g => g.Name == group);

            var vectorRequest = new ReindexRangeRequest
            {
                SidecarTable  = VectorSidecarNaming.TableName(model.TableName, vectorOptions),
                SidecarSchema = vectorOptions.SidecarSchema ?? model.Schema,
                SourceTable   = model.TableName,
                SourceSchema  = model.Schema,
                IdColumn      = model.KeyColumnName,
                KeyColumns    = [.. model.Key.Select(k => k.ColumnName)],
                ColumnSuffix  = vectorOptions.ColumnSuffix,
                Groups        = [],
                VectorGroups  = [vgroup],
                BatchSize     = options.BatchSize ?? vectorOptions.ConcurrentBatchSize,
                BatchDelay    = options.BatchDelay ?? vectorOptions.ConcurrentBatchDelay,
                StartAfterId  = options.StartAfterId,
            };

            var vectorConnection = (NpgsqlConnection)await session.OpenConnectionAsync(ct);
            var provider = vectorBackend.EmbeddingProvider;
            var columnName = VectorSidecarNaming.ColumnName(vgroup.Name, vectorOptions.ColumnSuffix, VectorSidecarNaming.CurrentVersion);
            VectorVersionRegistry.EnsureConfigDims(provider.Dimensions, vgroup.Dimensions, model.TableName, vgroup.Name);
            await new ReindexJobProcessor().RunVectorRangeAsync(
                vectorConnection, vectorRequest, provider, ct);

            await VectorVersionRegistry.UpsertActiveAsync(
                vectorConnection, model.TableName, vgroup.Name,
                provider.ModelId, provider.Dimensions, columnName, ct);
            return;
        }

        var fullText = _backends.OfType<FullTextSearchBackend>().FirstOrDefault()
            ?? throw new InvalidOperationException(
                "ReindexAsync requires the full-text search backend to be registered.");
        var ftOptions = fullText.Options;

        if (!model.FullTextGroups.Any(g => g.Name == group))
            throw new InvalidOperationException(
                $"Entity '{model.TableName}' has no full-text group named '{group}'.");

        var (batchSize, batchDelay) = options.Resolve(ftOptions);

        var request = new ReindexRangeRequest
        {
            SidecarTable  = FullTextSidecarNaming.TableName(model.TableName, ftOptions),
            SidecarSchema = ftOptions.SidecarSchema,
            SourceTable   = model.TableName,
            SourceSchema  = model.Schema,
            IdColumn      = model.KeyColumnName,
            KeyColumns    = [.. model.Key.Select(k => k.ColumnName)],
            ColumnSuffix  = ftOptions.ColumnSuffix,
            Groups        = model.FullTextGroups,
            BatchSize     = batchSize,
            BatchDelay    = batchDelay,
            StartAfterId  = options.StartAfterId,
        };

        var connection = (NpgsqlConnection)await session.OpenConnectionAsync(ct);

        var processor = new ReindexJobProcessor();
        await processor.RunRangeAsync(connection, request, onBatchCommitted: null, ct);
    }

    // Cursor-wrapped offset: when a cursor query carries a search term we cannot use
    // keyset pagination (rank order is opaque), so we route through the offset-search
    // machinery and carry the absolute offset inside the opaque cursor token (Version 2).
    //
    // Direction note: the wrapped-offset cursor encodes an ABSOLUTE position. Next/Prev
    // cursors already point at the correct absolute offset, so CursorDirection is ignored
    // on this path — feeding a PrevCursor naturally lands on the previous page.
    private async Task<CursorResult<T>> ExecuteSearchCursorAsync<T, TKey>(
        IFerretSession session, EntityModel model, EntityMetadata meta,
        PagedQuery<T, TKey> query, int limit, Activity? parent, CancellationToken ct)
        where T : class where TKey : notnull
    {
        var fp = CursorFingerprint.Compute(meta.TableName, query.Sort, query.Filter,
            [.. meta.Key.Select(k => k.ColumnName)], searchTerm: query.Search!.Trim());

        var offset = 0;
        if (!string.IsNullOrEmpty(query.Cursor))
        {
            CursorPayload decoded;
            try { decoded = CursorToken.Decode(query.Cursor); }
            catch (FormatException)
            {
                throw new InvalidCursorException("cursor token is malformed; restart without cursor");
            }
            if (decoded.Version != 2 || decoded.Fingerprint != fp)
                throw new InvalidCursorException("cursor invalid for current search/sort/filter; restart without cursor");
            offset = decoded.Offset ?? 0;
        }

        var maxOffset = _hybridOptions?.MaxSearchCursorOffset ?? 200;
        if (offset > maxOffset)
            throw new InvalidCursorException("cursor offset exceeds the maximum search-cursor offset; restart without cursor");
        parent?.SetTag(Tags.CursorDir, "forward");

        var result = await ExecuteOffsetSearchCoreAsync<T, TKey>(session, model, meta, query, offset, limit, ct);
        var items = result.Items;

        var (nextCursor, hasMore) = ComputeNextSearchCursor(offset, limit, items.Count, maxOffset, fp);

        string? prevCursor = offset > 0
            ? CursorToken.Encode(new CursorPayload { Version = 2, Offset = Math.Max(0, offset - limit), Fingerprint = fp })
            : null;
        var hasPrev = offset > 0;

        return new CursorResult<T>
        {
            Items = items,
            Limit = limit,
            NextCursor = nextCursor,
            PrevCursor = prevCursor,
            HasMore = hasMore,
            HasPrev = hasPrev,
            MatchInfo = null,
        };
    }

    private async Task<CursorResult<T>> ExecuteCursorAsync<T, TKey>(
        IFerretSession session, EntityModel model, EntityMetadata meta,
        PagedQuery<T, TKey> query, int limit, Activity? parent, CancellationToken ct)
        where T : class where TKey : notnull
    {
        var expectedFp = CursorFingerprint.Compute(meta.TableName, query.Sort, query.Filter,
            [.. meta.Key.Select(k => k.ColumnName)]);

        CursorPayload? decoded = null;
        var direction = query.CursorDirection;
        if (!string.IsNullOrEmpty(query.Cursor))
        {
            try { decoded = CursorToken.Decode(query.Cursor); }
            catch (FormatException)
            {
                throw new InvalidCursorException("cursor token is malformed; restart without cursor");
            }
            if (decoded.Fingerprint != expectedFp)
                throw new InvalidCursorException("cursor invalid for current sort/filter; restart without cursor");
            if (direction == CursorDirection.None)
                direction = CursorDirection.Forward;
        }
        parent?.SetTag(Tags.CursorDir, direction.ToString().ToLowerInvariant());

        var sortWithTiebreaker = PagedSqlBuilder.EnsureTiebreaker(meta, query.Sort);

        var filterFragments = new List<SqlFragment>();
        var paramIndex = 0;
        foreach (var f in query.Filter)
        {
            var frag = PagedSqlBuilder.CompileFilter(f, meta, paramIndex);
            filterFragments.Add(frag);
            paramIndex += frag.Parameters.Count;
        }

        SqlFragment? cursorPredicate = null;
        if (decoded is not null)
        {
            cursorPredicate = PagedSqlBuilder.BuildCursorPredicate(
                meta, sortWithTiebreaker, decoded, direction, paramIndex);
            paramIndex += cursorPredicate.Value.Parameters.Count;
        }

        var keyColsSelect = string.Join(", ", meta.Key.Select(k => meta.Dialect.QuoteIdentifier(k.ColumnName)));
        var sb = new StringBuilder();
        sb.Append("SELECT ").Append(keyColsSelect);
        sb.Append(" FROM ").Append(meta.QuotedTable);

        var where = new List<string>();
        foreach (var f in filterFragments) where.Add(f.Sql);
        if (cursorPredicate is { } cp) where.Add(cp.Sql);
        if (where.Count > 0) sb.Append(" WHERE ").Append(string.Join(" AND ", where));

        sb.Append(" ORDER BY ");
        for (int i = 0; i < sortWithTiebreaker.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            var sortClause = sortWithTiebreaker[i];
            var col = meta.Dialect.QuoteIdentifier(meta.ColumnByPropertyName[sortClause.Field]);
            var baseDir = sortClause.Direction == SortDirection.Descending ? "DESC" : "ASC";
            var emitDir = direction == CursorDirection.Backward
                ? (baseDir == "DESC" ? "ASC" : "DESC")
                : baseDir;
            sb.Append(col).Append(' ').Append(emitDir);
        }
        sb.Append(' ').Append(meta.Dialect.PagingClause(limit + 1, 0));

        var allParams = new List<object?>();
        foreach (var f in filterFragments) allParams.AddRange(f.Parameters);
        if (cursorPredicate is { } cp2) allParams.AddRange(cp2.Parameters);

        var connection = await session.OpenConnectionAsync(ct);
        var commandText = sb.ToString();

        var rankedIds = await ExecuteIdReaderAsync<TKey>(
            connection, "ferret.db.query.ids", commandText, allParams, meta.Key.Count, ct);
        var totalCount = 0;

        var fetchedExtra = rankedIds.Count > limit;
        if (fetchedExtra) rankedIds = rankedIds.Take(limit).ToList();
        if (direction == CursorDirection.Backward) rankedIds.Reverse();

        if (rankedIds.Count == 0)
        {
            return new CursorResult<T>
            {
                Items = [],
                Limit = limit,
                NextCursor = null,
                PrevCursor = null,
                HasMore = false,
                HasPrev = false,
                MatchInfo = null,
            };
        }

        var ordered = await HydrateOrderedAsync<T, TKey>(session, connection, meta, model, rankedIds, ct);

        var sortFieldProps = sortWithTiebreaker
            .Take(sortWithTiebreaker.Count - meta.Key.Count)
            .Select(s => typeof(T).GetProperty(s.Field) ?? throw new InvalidOperationException(
                $"Sort field '{s.Field}' has no matching CLR property on '{typeof(T).Name}'."))
            .ToList();
        var keyProps = model.Key
            .Select(k => (typeof(T).GetProperty(k.PropertyName)!, k.ClrType))
            .ToList();

        string? nextCursor = null;
        string? prevCursor = null;
        if (ordered.Count > 0)
        {
            nextCursor = EncodeAnchor(ordered[^1], sortFieldProps, keyProps, expectedFp);
            prevCursor = EncodeAnchor(ordered[0], sortFieldProps, keyProps, expectedFp);
        }

        bool hasMore;
        bool hasPrev;
        if (direction == CursorDirection.Backward)
        {
            hasPrev = fetchedExtra;
            hasMore = decoded is not null;
        }
        else
        {
            hasMore = fetchedExtra;
            hasPrev = decoded is not null;
        }

        _ = totalCount;
        return new CursorResult<T>
        {
            Items = ordered,
            Limit = limit,
            NextCursor = nextCursor,
            PrevCursor = prevCursor,
            HasMore = hasMore,
            HasPrev = hasPrev,
            MatchInfo = null,
        };
    }

    internal static (string? NextCursor, bool HasMore) ComputeNextSearchCursor(
        int offset, int limit, int fetchedCount, int maxOffset, string fingerprint)
    {
        var nextOffset = offset + limit;
        if (fetchedCount < limit || nextOffset > maxOffset)
            return (null, false);
        var next = CursorToken.Encode(new CursorPayload { Version = 2, Offset = nextOffset, Fingerprint = fingerprint });
        return (next, true);
    }

    internal static string EncodeAnchor<T>(
        T row,
        IReadOnlyList<PropertyInfo> sortFieldProps,
        IReadOnlyList<(PropertyInfo Prop, Type ClrType)> keyProps,
        string fingerprint)
        where T : class
    {
        var sortKeys = new List<string>(sortFieldProps.Count);
        foreach (var p in sortFieldProps)
        {
            var v = p.GetValue(row);
            sortKeys.Add(v?.ToString() ?? "");
        }
        var parts = new (object value, Type type)[keyProps.Count];
        for (var i = 0; i < keyProps.Count; i++)
            parts[i] = (keyProps[i].Prop.GetValue(row)!, keyProps[i].ClrType);
        var payload = new CursorPayload
        {
            Version = 1,
            SortKeys = sortKeys,
            PrimaryKeys = [.. CursorPrimaryKey.Encode(parts)],
            Fingerprint = fingerprint,
        };
        return CursorToken.Encode(payload);
    }

    private async Task<OffsetResult<T>> ExecuteOffsetStandardAsync<T, TKey>(
        IFerretSession session, EntityModel model, EntityMetadata meta,
        PagedQuery<T, TKey> query, int page, int limit, CancellationToken ct)
        where T : class where TKey : notnull
    {
        var filterFragments = new List<SqlFragment>();
        var paramIndex = 0;
        foreach (var f in query.Filter)
        {
            var frag = PagedSqlBuilder.CompileFilter(f, meta, paramIndex);
            filterFragments.Add(frag);
            paramIndex += frag.Parameters.Count;
        }
        var sortFragments = query.Sort.Select(s => PagedSqlBuilder.CompileSort(s, meta)).ToList();

        var idsAndCount = PagedSqlBuilder.BuildSelectIdsAndCount(
            meta, filterFragments, sortFragments, page, limit, candidateIds: null);

        var connection = await session.OpenConnectionAsync(ct);
        var (rankedIds, totalCount) = await ExecuteIdsAndCountAsync<TKey>(
            connection, "ferret.db.query.ids", idsAndCount.Sql, idsAndCount.Parameters, meta.Key.Count, ct);

        if (rankedIds.Count == 0)
        {
            return new OffsetResult<T>
            {
                Items = [],
                Limit = limit,
                Page = page,
                TotalCount = totalCount,
                HasMore = false,
                HasPrev = page > 0,
                MatchInfo = null,
            };
        }

        var ordered = await HydrateOrderedAsync<T, TKey>(session, connection, meta, model, rankedIds, ct);
        return new OffsetResult<T>
        {
            Items = ordered,
            Limit = limit,
            Page = page,
            TotalCount = totalCount,
            HasMore = totalCount > (page + 1) * limit,
            HasPrev = page > 0,
            MatchInfo = null,
        };
    }

    internal HybridConfig? ResolveHybrid(EntityModel model, SearchBackend? forced) =>
        model.HybridConfig is not null && forced is null ? model.HybridConfig : null;

    private ISearchBackend ResolveBackendByEnum(EntityModel model, SearchBackend backend)
    {
        var name = backend switch
        {
            SearchBackend.Trigram  => "trigram",
            SearchBackend.FullText => "fulltext",
            SearchBackend.Vector   => "vector",
            _ => throw new InvalidOperationException($"Backend {backend} is not supported in v1."),
        };
        return _backends.FirstOrDefault(b => b.Name == name)
            ?? throw new InvalidOperationException(
                $"Search backend '{name}' requested for entity '{model.TableName}' is not registered.");
    }

    internal ISearchBackend ResolveBackend(EntityModel model)
    {
        var backends = model.SearchableProperties.Select(p => p.Backend).Distinct().ToList();
        if (backends.Count == 0)
            throw new InvalidOperationException("Search invoked on an entity with no [Searchable] properties.");

        if (backends.Count == 1)
        {
            var name = backends[0] switch
            {
                SearchBackend.Trigram  => "trigram",
                SearchBackend.FullText => "fulltext",
                SearchBackend.Vector   => "vector",
                _ => throw new InvalidOperationException($"Backend {backends[0]} is not supported in v1."),
            };
            return _backends.FirstOrDefault(b => b.Name == name)
                ?? throw new InvalidOperationException(
                    $"Search backend '{name}' required by entity '{model.TableName}' is not registered.");
        }

        // Multi-backend on one entity: pick the one flagged AsPrimary.
        var primaries = _backends.OfType<IAsPrimaryAware>().Where(b => b.IsPrimary).ToList();
        if (primaries.Count != 1)
            throw new InvalidOperationException(
                $"Entity '{model.TableName}' has multiple backends; exactly one must be configured with AsPrimary().");

        return (ISearchBackend)primaries[0];
    }

    /// <summary>
    /// Restricts searchable properties to the requested fields. A field matches a property by
    /// column name or CLR property name (case-insensitive) at any join depth, or by the
    /// qualified <c>table.column</c> form for joined properties. Unknown names match nothing;
    /// an all-unknown list therefore yields an empty result (legacy-compatible semantics).
    /// </summary>
    private static IReadOnlyList<SearchablePropertyInfo> FilterBySearchFields(
        IReadOnlyList<SearchablePropertyInfo> properties, IReadOnlyList<string> searchFields)
    {
        if (searchFields.Count == 0) return properties;

        return properties.Where(p => searchFields.Any(f =>
        {
            var dot = f.IndexOf('.');
            if (dot > 0)
            {
                return !p.JoinPath.IsDirect
                    && string.Equals(p.OwnerTableName, f[..dot], StringComparison.OrdinalIgnoreCase)
                    && string.Equals(p.ColumnName, f[(dot + 1)..], StringComparison.OrdinalIgnoreCase);
            }
            return string.Equals(p.ColumnName, f, StringComparison.OrdinalIgnoreCase)
                || string.Equals(p.Property.Name, f, StringComparison.OrdinalIgnoreCase);
        })).ToList();
    }

    private static SearchSqlFragment BuildSearchFragment(
        ISearchBackend backend, EntityModel model, ISqlDialect dialect, string term,
        float[]? searchVector, int limit, int offset, bool hasCandidateIds,
        EntityMetadata meta, IReadOnlyList<string>? candidateKeyParameterNames,
        string? resolvedVectorColumn, IReadOnlyList<string> searchFields)
    {
        var keyColumns = meta.IsComposite ? meta.Key.Select(k => k.ColumnName).ToList() : null;

        if (backend is FullTextSearchBackend ft)
        {
            return ft.BuildRanking(new FullTextSqlContext
            {
                SourceTable = model.TableName,
                IdColumn = model.KeyColumnName,
                SearchTerm = term,
                Groups = model.FullTextGroups,
                Limit = limit,
                Offset = offset,
                SidecarSchema = ft.SidecarSchema,
                CandidateIdsParameterName = hasCandidateIds ? "@candidate_ids" : null,
                KeyColumns = keyColumns,
                CandidateKeyParameterNames = hasCandidateIds ? candidateKeyParameterNames : null,
            });
        }

        if (backend is VectorSearchBackend vb)
        {
            return vb.BuildRanking(new VectorSqlContext
            {
                SidecarTable = VectorSidecarNaming.TableName(model.TableName, vb.Options),
                SidecarSchema = vb.SidecarSchema,
                GroupColumn = resolvedVectorColumn!,
                IdColumn = model.KeyColumnName,
                KeyColumns = keyColumns,
                Limit = limit,
                Offset = offset,
                QueryVectorParameterName = "@qvec",
                EfSearch = vb.Options.EfSearch,
                CandidateIdsParameterName = hasCandidateIds ? "@candidate_ids" : null,
                CandidateKeyParameterNames = hasCandidateIds ? candidateKeyParameterNames : null,
            });
        }

        // Field restriction applies to property-level backends only; full-text groups and
        // vector sidecars aggregate at index time and cannot filter per source field.
        var ctx = new SearchContext
        {
            Properties = FilterBySearchFields(model.SearchableProperties, searchFields),
            SearchTerm = term,
            IdColumn = model.KeyColumnName,
            QuotedTable = model.QuotedTable(dialect),
            HasCandidateIds = hasCandidateIds,
            KeyColumns = keyColumns,
            CandidateKeyParameterNames = hasCandidateIds ? candidateKeyParameterNames : null,
        };
        return backend.BuildRankingQuery(ctx);
    }

    private static async Task<string> ResolveActiveVectorColumnAsync(
        NpgsqlConnection connection, EntityModel model, VectorGroup group,
        VectorSearchBackend vb, CancellationToken ct)
    {
        var entity = model.TableName;
        var provider = vb.EmbeddingProvider;
        VectorVersionRegistry.EnsureConfigDims(provider.Dimensions, group.Dimensions, entity, group.Name);
        var active = await VectorVersionRegistry.GetActiveAsync(connection, entity, group.Name, ct);
        VectorVersionRegistry.EnsureMatch(active, entity, group.Name, provider.ModelId, provider.Dimensions);
        return active!.ColumnName;
    }

    private async Task<string> GetActiveVectorColumnAsync(
        NpgsqlConnection connection, EntityModel model, VectorGroup group,
        VectorSearchBackend vb, CancellationToken ct)
    {
        var key = (model.TableName, group.Name);
        if (_activeVectorColumns.TryGetValue(key, out var cached))
            return cached;

        var column = await ResolveActiveVectorColumnAsync(connection, model, group, vb, ct);
        _activeVectorColumns.TryAdd(key, column);
        return column;
    }

    private (SearchSqlFragment Fragment, bool VectorParticipates) BuildHybridFragment(
        EntityModel model, HybridConfig config, HybridOptions opts, ISqlDialect dialect,
        string term, int limit, int offset, EntityMetadata meta, string? resolvedVectorColumn)
    {
        var keyColumns = meta.IsComposite
            ? meta.Key.Select(k => k.ColumnName).ToList()
            : new List<string> { model.KeyColumnName };

        var depth = (limit + offset) * opts.CandidateDepth;
        if (depth <= 0) depth = opts.CandidateDepth;

        var vectorParticipates = false;
        var fragments = new List<HybridBackendFragment>(config.Backends.Count);

        for (var i = 0; i < config.Backends.Count; i++)
        {
            var bc = config.Backends[i];

            // Effective weight: NaN means inherit the global default.
            var weight = double.IsNaN(bc.Weight) ? opts.DefaultWeight : bc.Weight;

            // Effective confidence threshold: NaN means inherit the global default.
            double? threshold = double.IsNaN(bc.ConfidenceThreshold)
                ? opts.DefaultConfidenceThreshold
                : bc.ConfidenceThreshold;

            var cteName = bc.Backend switch
            {
                SearchBackend.Trigram  => "trgm_c",
                SearchBackend.FullText => "ft_c",
                SearchBackend.Vector   => "vec_c",
                _ => throw new InvalidOperationException($"Backend {bc.Backend} is not supported in hybrid search."),
            };

            var req = new RankedCandidateRequest
            {
                SourceTable = model.TableName,
                SidecarSchema = null,
                KeyColumns = keyColumns,
                SearchTerm = term,
                Depth = depth,
                ConfidenceThreshold = threshold,
                CteName = cteName,
                QueryVectorParameterName = bc.Backend == SearchBackend.Vector ? "@qvec" : null,
            };

            SearchSqlFragment body;
            switch (bc.Backend)
            {
                case SearchBackend.FullText:
                {
                    var ft = (FullTextSearchBackend)ResolveBackendByEnum(model, SearchBackend.FullText);
                    var ftCtx = new FullTextSqlContext
                    {
                        SourceTable = model.TableName,
                        IdColumn = model.KeyColumnName,
                        SearchTerm = term,
                        Groups = model.FullTextGroups,
                        Limit = limit,
                        Offset = offset,
                        SidecarSchema = ft.SidecarSchema,
                        KeyColumns = meta.IsComposite ? keyColumns : null,
                    };
                    body = ft.BuildRankedCandidate(req with { SidecarSchema = ft.SidecarSchema }, ftCtx);
                    break;
                }
                case SearchBackend.Vector:
                {
                    vectorParticipates = true;
                    var vb = (VectorSearchBackend)ResolveBackendByEnum(model, SearchBackend.Vector);
                    var sidecarTable = VectorSidecarNaming.TableName(model.TableName, vb.Options);
                    var groupColumn = resolvedVectorColumn!;
                    body = vb.BuildRankedCandidate(
                        req with { SidecarSchema = vb.SidecarSchema }, sidecarTable, groupColumn);
                    break;
                }
                case SearchBackend.Trigram:
                {
                    var tg = (TrigramSearchBackend)ResolveBackendByEnum(model, SearchBackend.Trigram);
                    var sctx = new SearchContext
                    {
                        Properties = model.SearchableProperties,
                        SearchTerm = term,
                        IdColumn = model.KeyColumnName,
                        QuotedTable = model.QuotedTable(dialect),
                        HasCandidateIds = false,
                        KeyColumns = meta.IsComposite ? keyColumns : null,
                    };
                    body = tg.BuildRankedCandidate(req, sctx);
                    break;
                }
                default:
                    throw new InvalidOperationException($"Backend {bc.Backend} is not supported in hybrid search.");
            }

            fragments.Add(new HybridBackendFragment
            {
                CteName = cteName,
                Body = body,
                Weight = weight,
            });
        }

        var hybridCtx = new HybridSqlContext
        {
            KeyColumns = keyColumns,
            Limit = limit,
            Offset = offset,
            RrfK = opts.RrfK,
            Backends = fragments,
        };

        var fragment = new HybridSqlBuilder(dialect).Build(hybridCtx);
        return (fragment, vectorParticipates);
    }

    private static Array[] BuildCandidateKeyArrays<TKey>(EntityMetadata meta, List<TKey> candidateKeys)
        where TKey : notnull
    {
        var arrays = new Array[meta.Key.Count];
        for (var col = 0; col < meta.Key.Count; col++)
        {
            var typed = Array.CreateInstance(meta.Key[col].ClrType, candidateKeys.Count);
            for (var row = 0; row < candidateKeys.Count; row++)
            {
                var parts = (object[])(object)candidateKeys[row]!;
                typed.SetValue(parts[col], row);
            }
            arrays[col] = typed;
        }
        return arrays;
    }

    private Task<OffsetResult<T>> ExecuteOffsetSearchAsync<T, TKey>(
        IFerretSession session, EntityModel model, EntityMetadata meta,
        PagedQuery<T, TKey> query, int page, int limit, CancellationToken ct)
        where T : class where TKey : notnull =>
        ExecuteOffsetSearchCoreAsync<T, TKey>(session, model, meta, query, page * limit, limit, ct);

    private async Task<OffsetResult<T>> ExecuteOffsetSearchCoreAsync<T, TKey>(
        IFerretSession session, EntityModel model, EntityMetadata meta,
        PagedQuery<T, TKey> query, int offset, int limit, CancellationToken ct)
        where T : class where TKey : notnull
    {
        var page = limit > 0 ? offset / limit : 0;

        if (ResolveHybrid(model, query.Backend) is { } hybridConfig)
            return await ExecuteOffsetHybridCoreAsync<T, TKey>(
                session, model, meta, hybridConfig, query, offset, limit, ct);

        var backend = query.Backend is { } forced
            ? ResolveBackendByEnum(model, forced)
            : ResolveBackend(model);

        using var backendActivity = FerretDiagnostics.ActivitySource.StartActivity("ferret.search.candidates", ActivityKind.Client);
        backendActivity?.SetTag(Tags.Backend, backend.Name);
        backendActivity?.SetTag(Tags.Entity, meta.TableName);

        var connection = await session.OpenConnectionAsync(ct);

        var candidateKeys = query.CandidateKeys?.ToList();
        if (query.Filter.Count > 0)
        {
            var filterFragments = new List<SqlFragment>();
            var filterParamIndex = 0;
            foreach (var f in query.Filter)
            {
                var frag = PagedSqlBuilder.CompileFilter(f, meta, filterParamIndex);
                filterFragments.Add(frag);
                filterParamIndex += frag.Parameters.Count;
            }
            var keyColsSelect = string.Join(", ", meta.Key.Select(k => meta.Dialect.QuoteIdentifier(k.ColumnName)));
            var filterSql = $"SELECT {keyColsSelect} FROM {meta.QuotedTable} WHERE " +
                            string.Join(" AND ", filterFragments.Select(f => f.Sql));
            var filterParams = filterFragments.SelectMany(f => f.Parameters).ToList();

            var matched = await ExecuteIdReaderAsync<TKey>(
                connection, "ferret.db.query.filter", filterSql, filterParams, meta.Key.Count, ct);
            // External CandidateKeys and clause-derived candidates combine by intersection.
            candidateKeys = candidateKeys is null ? matched : [.. candidateKeys.Intersect(matched)];
            backendActivity?.SetTag(Tags.FilterCount, query.Filter.Count);
        }

        if (candidateKeys is { Count: 0 })
        {
            return new OffsetResult<T>
            {
                Items = [],
                Limit = limit,
                Page = page,
                TotalCount = 0,
                HasMore = false,
                HasPrev = page > 0,
                MatchInfo = null,
            };
        }

        var candidateKeyParameterNames = meta.IsComposite
            ? Enumerable.Range(0, meta.Key.Count).Select(i => $"@candidate_k{i}").ToList()
            : null;

        var vectorBackend = backend as VectorSearchBackend;
        var searchVector = vectorBackend is not null
            ? await vectorBackend.ResolveQueryVectorAsync(query.Search!.Trim(), ct)
            : null;

        string? resolvedVectorColumn = null;
        if (vectorBackend is not null)
            resolvedVectorColumn = await GetActiveVectorColumnAsync(
                (NpgsqlConnection)connection, model, model.VectorGroups[0] /* single-group v1 */, vectorBackend, ct);

        var fragment = BuildSearchFragment(
            backend, model, session.Dialect, query.Search!.Trim(),
            searchVector, limit, offset, candidateKeys is not null,
            meta, candidateKeyParameterNames, resolvedVectorColumn, query.SearchFields);

        var executionParams = fragment.Parameters.ToList();
        if (vectorBackend is not null && searchVector is not null)
            executionParams.Add(new KeyValuePair<string, object?>("qvec", FormatVector(searchVector)));
        if (candidateKeys is not null)
        {
            if (meta.IsComposite)
            {
                var arrays = BuildCandidateKeyArrays(meta, candidateKeys);
                for (var i = 0; i < arrays.Length; i++)
                    executionParams.Add(new KeyValuePair<string, object?>(candidateKeyParameterNames![i], arrays[i]));
            }
            else
            {
                executionParams.Add(new KeyValuePair<string, object?>("@candidate_ids", candidateKeys.ToArray()));
            }
        }

        var (rankedIds, totalCount) = await ExecuteIdsAndCountNamedAsync<TKey>(
            connection, "ferret.db.query.search", fragment.Sql, executionParams, meta.Key.Count, ct,
            efSearch: vectorBackend?.Options.EfSearch);
        backendActivity?.SetTag(Tags.RowCount, rankedIds.Count);

        if (rankedIds.Count == 0)
        {
            return new OffsetResult<T>
            {
                Items = [],
                Limit = limit,
                Page = page,
                TotalCount = totalCount,
                HasMore = false,
                HasPrev = page > 0,
                MatchInfo = null,
            };
        }

        var ordered = await HydrateOrderedAsync<T, TKey>(session, connection, meta, model, rankedIds, ct);
        return new OffsetResult<T>
        {
            Items = ordered,
            Limit = limit,
            Page = page,
            TotalCount = totalCount,
            HasMore = totalCount > (page + 1) * limit,
            HasPrev = page > 0,
            MatchInfo = null,
        };
    }

    private async Task<OffsetResult<T>> ExecuteOffsetHybridCoreAsync<T, TKey>(
        IFerretSession session, EntityModel model, EntityMetadata meta, HybridConfig config,
        PagedQuery<T, TKey> query, int offset, int limit, CancellationToken ct)
        where T : class where TKey : notnull
    {
        var page = limit > 0 ? offset / limit : 0;

        var opts = _hybridOptions
            ?? throw new InvalidOperationException(
                "Hybrid entity searched but UseHybridSearch() was not called.");

        // v1 limitation: filter + hybrid fusion is not supported. The per-backend ranked
        // candidate fragments do not accept candidate-id restriction, so a pre-pass filter
        // cannot be intersected with the fused result. Use a per-query Backend override to
        // search a single backend with filters.
        if (query.Filter.Count > 0)
            throw new NotSupportedException(
                "Filter + hybrid search is not supported in v1; use a per-query Backend override.");

        using var backendActivity = FerretDiagnostics.ActivitySource.StartActivity("ferret.search.candidates", ActivityKind.Client);
        backendActivity?.SetTag(Tags.Backend, "hybrid");
        backendActivity?.SetTag(Tags.Entity, meta.TableName);

        var connection = await session.OpenConnectionAsync(ct);
        var term = query.Search!.Trim();

        string? resolvedVectorColumn = null;
        if (config.Backends.Any(b => b.Backend == SearchBackend.Vector))
        {
            var vb = (VectorSearchBackend)ResolveBackendByEnum(model, SearchBackend.Vector);
            resolvedVectorColumn = await GetActiveVectorColumnAsync(
                (NpgsqlConnection)connection, model, model.VectorGroups[0] /* single-group v1 */, vb, ct);
        }

        var (fragment, vectorParticipates) = BuildHybridFragment(
            model, config, opts, session.Dialect, term, limit, offset, meta, resolvedVectorColumn);

        var executionParams = fragment.Parameters.ToList();
        VectorSearchBackend? vectorBackend = null;
        if (vectorParticipates)
        {
            vectorBackend = (VectorSearchBackend)ResolveBackendByEnum(model, SearchBackend.Vector);
            var searchVector = await vectorBackend.ResolveQueryVectorAsync(term, ct);
            if (searchVector is not null)
                executionParams.Add(new KeyValuePair<string, object?>("qvec", FormatVector(searchVector)));
        }

        var (rankedIds, totalCount) = await ExecuteIdsAndCountNamedAsync<TKey>(
            connection, "ferret.db.query.search", fragment.Sql, executionParams, meta.Key.Count, ct,
            efSearch: vectorParticipates ? vectorBackend!.Options.EfSearch : null);
        backendActivity?.SetTag(Tags.RowCount, rankedIds.Count);

        if (rankedIds.Count == 0)
        {
            return new OffsetResult<T>
            {
                Items = [],
                Limit = limit,
                Page = page,
                TotalCount = totalCount,
                HasMore = false,
                HasPrev = page > 0,
                MatchInfo = null,
            };
        }

        var ordered = await HydrateOrderedAsync<T, TKey>(session, connection, meta, model, rankedIds, ct);
        return new OffsetResult<T>
        {
            Items = ordered,
            Limit = limit,
            Page = page,
            TotalCount = totalCount,
            HasMore = totalCount > (page + 1) * limit,
            HasPrev = page > 0,
            MatchInfo = null,
        };
    }

    private async Task<List<TKey>> ExecuteIdReaderAsync<TKey>(
        DbConnection connection, string activityName, string sql,
        IReadOnlyList<object?> parameters, int keyCount, CancellationToken ct)
        where TKey : notnull
    {
        using var activity = FerretDiagnostics.ActivitySource.StartActivity(activityName, ActivityKind.Client);
        activity?.SetTag(Tags.DbSystem, "postgresql");
        if (_options.LogStatements) activity?.SetTag(Tags.DbStatement, sql);
        LogStatementDebug(sql);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        for (var i = 0; i < parameters.Count; i++)
            command.AddParameter($"@p{i}", parameters[i]);

        var ids = new List<TKey>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            ids.Add(ReadKey<TKey>(reader, keyCount));
        activity?.SetTag(Tags.RowCount, ids.Count);
        return ids;
    }

    private async Task<(List<TKey> Ids, int TotalCount)> ExecuteIdsAndCountAsync<TKey>(
        DbConnection connection, string activityName, string sql,
        IReadOnlyList<object?> parameters, int keyCount, CancellationToken ct)
        where TKey : notnull
    {
        using var activity = FerretDiagnostics.ActivitySource.StartActivity(activityName, ActivityKind.Client);
        activity?.SetTag(Tags.DbSystem, "postgresql");
        if (_options.LogStatements) activity?.SetTag(Tags.DbStatement, sql);
        LogStatementDebug(sql);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        for (var i = 0; i < parameters.Count; i++)
            command.AddParameter($"@p{i}", parameters[i]);

        var ids = new List<TKey>();
        var total = 0;
        var countColumn = keyCount;
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            ids.Add(ReadKey<TKey>(reader, keyCount));
            if (total == 0) total = reader.GetInt32(countColumn);
        }
        activity?.SetTag(Tags.RowCount, ids.Count);
        activity?.SetTag(Tags.TotalCount, total);
        return (ids, total);
    }

    private async Task<(List<TKey> Ids, int TotalCount)> ExecuteIdsAndCountNamedAsync<TKey>(
        DbConnection connection, string activityName, string sql,
        IReadOnlyList<KeyValuePair<string, object?>> parameters, int keyCount, CancellationToken ct,
        int? efSearch = null)
        where TKey : notnull
    {
        using var activity = FerretDiagnostics.ActivitySource.StartActivity(activityName, ActivityKind.Client);
        activity?.SetTag(Tags.DbSystem, "postgresql");
        if (_options.LogStatements) activity?.SetTag(Tags.DbStatement, sql);
        LogStatementDebug(sql);

        // Vector queries must run inside an explicit transaction so that the
        // SET LOCAL hnsw.ef_search applies to the ranking SELECT on the same
        // connection/transaction (otherwise Npgsql may split the batch and the
        // session-local setting is lost).
        DbTransaction? tx = null;
        if (efSearch is { } ef)
        {
            tx = await connection.BeginTransactionAsync(ct);
            await using var setCmd = connection.CreateCommand();
            setCmd.Transaction = tx;
            setCmd.CommandText = $"SET LOCAL hnsw.ef_search = {ef};";
            await setCmd.ExecuteNonQueryAsync(ct);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = tx;
            command.CommandText = sql;
            foreach (var kvp in parameters)
                command.AddParameter(kvp.Key, kvp.Value);

            var ids = new List<TKey>();
            var total = 0;
            var countColumn = keyCount;
            // Read and fully drain the reader inside its own scope so it is disposed
            // before the transaction is committed — committing while a reader is still
            // open on the same connection throws "a command is already in progress".
            await using (var reader = await command.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    ids.Add(ReadKey<TKey>(reader, keyCount));
                    if (total == 0) total = reader.GetInt32(countColumn);
                }
            }
            activity?.SetTag(Tags.RowCount, ids.Count);
            activity?.SetTag(Tags.TotalCount, total);

            if (tx is not null) await tx.CommitAsync(ct);
            return (ids, total);
        }
        finally
        {
            if (tx is not null) await tx.DisposeAsync();
        }
    }

    private static string FormatVector(float[] vector) =>
        "[" + string.Join(",", vector.Select(f => f.ToString(CultureInfo.InvariantCulture))) + "]";

    private static TKey ReadKey<TKey>(DbDataReader reader, int keyCount)
        where TKey : notnull
    {
        if (keyCount == 1)
            return (TKey)reader.GetValue(0);

        var parts = new object[keyCount];
        for (var i = 0; i < keyCount; i++)
            parts[i] = reader.GetValue(i);
        return (TKey)(object)parts;
    }

    private async Task<List<T>> HydrateOrderedAsync<T, TKey>(
        IFerretSession session, DbConnection connection, EntityMetadata meta, EntityModel model,
        List<TKey> rankedIds, CancellationToken ct)
        where T : class where TKey : notnull
    {
        using var activity = FerretDiagnostics.ActivitySource.StartActivity("ferret.hydrate", ActivityKind.Client);
        activity?.SetTag(Tags.Entity, meta.TableName);
        activity?.SetTag(Tags.RowCount, rankedIds.Count);

        var hydrationSql = BuildHydrationSql(meta);
        if (_options.LogStatements) activity?.SetTag(Tags.DbStatement, hydrationSql);
        LogStatementDebug(hydrationSql);

        var hydrationParams = BuildHydrationParameters(meta, rankedIds);
        var hydrated = await session.Hydrator.HydrateAsync<T>(connection,
            new HydrationRequest(hydrationSql, hydrationParams), ct);

        if (!meta.IsComposite)
        {
            var keyProp = typeof(T).GetProperty(model.KeyPropertyName)!;
            var lookup = hydrated.ToDictionary(e => (TKey)keyProp.GetValue(e)!);
            return rankedIds.Where(lookup.ContainsKey).Select(id => lookup[id]).ToList();
        }

        var keyProps = model.Key.Select(k => typeof(T).GetProperty(k.PropertyName)!).ToArray();
        var compositeLookup = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var e in hydrated)
            compositeLookup[CompositeKeyString(keyProps.Select(p => p.GetValue(e)))] = e;
        return rankedIds
            .Select(id => CompositeKeyString((object[])(object)id!))
            .Where(compositeLookup.ContainsKey)
            .Select(k => compositeLookup[k])
            .ToList();
    }

    private static string CompositeKeyString(IEnumerable<object?> parts) =>
        string.Join('\u001f', parts.Select(p => p switch
        {
            Guid g => g.ToString("N"),
            IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            null => "\0",
            _ => p.ToString() ?? "",
        }));

    internal static string BuildHydrationSql(EntityMetadata meta)
    {
        if (!meta.IsComposite)
            return $"SELECT * FROM {meta.QuotedTable} WHERE {meta.Dialect.QuoteIdentifier(meta.IdColumnName)} = ANY({{0}})";

        var columns = string.Join(", ", meta.Key.Select(k => meta.Dialect.QuoteIdentifier(k.ColumnName)));
        var unnestArgs = string.Join(", ", Enumerable.Range(0, meta.Key.Count).Select(i => $"{{{i}}}"));
        return $"SELECT * FROM {meta.QuotedTable} WHERE ({columns}) IN (SELECT * FROM unnest({unnestArgs}))";
    }

    private static IReadOnlyList<object?> BuildHydrationParameters<TKey>(
        EntityMetadata meta, List<TKey> rankedIds)
        where TKey : notnull
    {
        if (!meta.IsComposite)
            return [rankedIds.ToArray()];

        var arrays = new object?[meta.Key.Count];
        for (var col = 0; col < meta.Key.Count; col++)
        {
            var typed = Array.CreateInstance(meta.Key[col].ClrType, rankedIds.Count);
            for (var row = 0; row < rankedIds.Count; row++)
            {
                var parts = (object[])(object)rankedIds[row]!;
                typed.SetValue(parts[col], row);
            }
            arrays[col] = typed;
        }
        return arrays;
    }

    private void LogStatementDebug(string sql)
    {
        if (_options.LogStatements && _logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("Ferret SQL: {Sql}", sql);
    }

    private void ObserveSlowQuery(Activity? activity, Stopwatch sw, string table, string mode)
    {
        sw.Stop();
        var ms = sw.Elapsed.TotalMilliseconds;
        activity?.SetTag("ferret.duration_ms", ms);
        if (_options.SlowQueryThresholdMs > 0 && ms >= _options.SlowQueryThresholdMs)
        {
            _logger.LogWarning(
                "Ferret slow query: table={Table} mode={Mode} duration_ms={Duration} threshold_ms={Threshold}",
                table, mode, ms, _options.SlowQueryThresholdMs);
        }
    }

    private void RecordFailure(Activity? activity, Exception ex, string table, string mode)
    {
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity?.AddTag("exception.type", ex.GetType().FullName);
        _logger.LogError(ex, "Ferret query failed: table={Table} mode={Mode}", table, mode);
    }
}
