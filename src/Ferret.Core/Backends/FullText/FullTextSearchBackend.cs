using Ferret.Abstractions.Attributes;
using Ferret.Abstractions.Search;
using Ferret.Abstractions.Sql;
using Ferret.Core.Backends.Hybrid;

namespace Ferret.Core.Backends.FullText;

internal interface IAsPrimaryAware
{
    bool IsPrimary { get; }
}

internal sealed class FullTextSearchBackend : ISearchBackend, IAsPrimaryAware
{
    private readonly ISqlDialect _dialect;
    private readonly FullTextOptions _options;
    private readonly FullTextSqlBuilder _builder;

    public FullTextSearchBackend(ISqlDialect dialect, FullTextOptions options)
    {
        _dialect = dialect;
        _options = options;
        _builder = new FullTextSqlBuilder(dialect, options);
    }

    public string Name => "fulltext";

    public bool CanHandle(SearchablePropertyInfo property) => property.Backend == SearchBackend.FullText;

    public SearchSqlFragment BuildRankingQuery(SearchContext context)
    {
        // SearchContext does not carry resolved groups; the engine routes fulltext queries
        // through the FullText-specific path (Task H1), bypassing this method. Throw to
        // fail loud if a caller accidentally invokes the trigram-style contract.
        throw new InvalidOperationException(
            "FullTextSearchBackend.BuildRankingQuery must be invoked via the fulltext engine path, not the trigram contract.");
    }

    public SearchIndexDefinition? GetIndexDefinition(SearchablePropertyInfo property) => null;

    public Task<float[]?> ResolveQueryVectorAsync(string searchTerm, CancellationToken ct) =>
        Task.FromResult<float[]?>(null);

    public bool IsPrimary => _options.AsPrimary;

    public string? SidecarSchema => _options.SidecarSchema;

    internal FullTextOptions Options => _options;

    internal SearchSqlFragment BuildRanking(FullTextSqlContext ctx) => _builder.BuildRanking(ctx);

    internal SearchSqlFragment BuildRankedCandidate(RankedCandidateRequest req, FullTextSqlContext ctx) =>
        _builder.BuildRankedCandidate(req, ctx);
}
