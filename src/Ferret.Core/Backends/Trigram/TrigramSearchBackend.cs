using Ferret.Core.Backends.FullText;
using Ferret.Core.Backends.Hybrid;

namespace Ferret.Core.Backends.Trigram;

internal sealed class TrigramSearchBackend : ISearchBackend, IAsPrimaryAware
{
    private readonly ISqlDialect _dialect;
    private readonly TrigramOptions _options;
    private readonly TrigramSqlBuilder _builder;

    public TrigramSearchBackend(ISqlDialect dialect, TrigramOptions options)
    {
        _dialect = dialect;
        _options = options;
        _builder = new TrigramSqlBuilder(dialect, options);
    }

    public string Name => "trigram";

    public bool CanHandle(SearchablePropertyInfo property) => property.Backend == SearchBackend.Trigram;

    public SearchSqlFragment BuildRankingQuery(SearchContext context) =>
        _builder.BuildRanking(context, page: 0, pageSize: int.MaxValue);          // engine clamps via PagedQuery.Page/PageSize

    internal SearchSqlFragment BuildRankedCandidate(RankedCandidateRequest req, SearchContext context) =>
        _builder.BuildRankedCandidate(req, context);

    public SearchIndexDefinition? GetIndexDefinition(SearchablePropertyInfo property)
    {
        // Use the resolved column name (naming strategy / [SearchColumn]) — lowercasing the
        // CLR name breaks multi-word columns ("DisplayName" is "display_name", not "displayname").
        var col = property.ColumnName;
        var table = "TBD";                                                        // resolved by engine before passing to schema package
        var idx = $"ix_{table}_{col}_gist_trgm";
        var sql = $"CREATE INDEX CONCURRENTLY IF NOT EXISTS {_dialect.QuoteIdentifier(idx)} " +
                  $"ON {_dialect.QuoteIdentifier(table)} USING gist (({_dialect.QuoteIdentifier(col)}::text) gist_trgm_ops);";
        return new SearchIndexDefinition
        {
            IndexName = idx,
            TableName = table,
            ColumnName = col,
            IndexSql = sql,
            RequiredExtensions = ["pg_trgm"],
        };
    }

    public Task<float[]?> ResolveQueryVectorAsync(string searchTerm, CancellationToken ct) =>
        Task.FromResult<float[]?>(null);

    public bool IsPrimary => false;
}
