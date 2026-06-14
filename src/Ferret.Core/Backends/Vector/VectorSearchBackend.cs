using Ferret.Abstractions.Embeddings;
using Ferret.Abstractions.Search;
using Ferret.Abstractions.Sql;
using Ferret.Core.Backends.FullText;
using Ferret.Core.Backends.Hybrid;

namespace Ferret.Core.Backends.Vector;

internal sealed class VectorSearchBackend : ISearchBackend, IAsPrimaryAware
{
    private readonly VectorOptions _options;
    private readonly IEmbeddingProvider _provider;
    private readonly VectorSqlBuilder _builder;

    public VectorSearchBackend(ISqlDialect dialect, VectorOptions options, IEmbeddingProvider provider)
    {
        _options = options;
        _provider = provider;
        _builder = new VectorSqlBuilder(dialect, options);
    }

    public string Name => "vector";

    public bool CanHandle(SearchablePropertyInfo property) => property.Backend == SearchBackend.Vector;

    public SearchSqlFragment BuildRankingQuery(SearchContext context) =>
        throw new InvalidOperationException(
            "VectorSearchBackend.BuildRankingQuery must be invoked via the vector engine path, not the trigram contract.");

    public SearchIndexDefinition? GetIndexDefinition(SearchablePropertyInfo property) => null;

    public async Task<float[]?> ResolveQueryVectorAsync(string searchTerm, CancellationToken ct) =>
        await _provider.EmbedAsync(searchTerm, ct);

    public bool IsPrimary => _options.AsPrimary;

    public string? SidecarSchema => _options.SidecarSchema;

    internal VectorOptions Options => _options;

    internal IEmbeddingProvider EmbeddingProvider => _provider;

    internal SearchSqlFragment BuildRanking(VectorSqlContext ctx) => _builder.BuildRanking(ctx);

    internal SearchSqlFragment BuildRankedCandidate(RankedCandidateRequest req, string sidecarTable, string groupColumn) =>
        _builder.BuildRankedCandidate(req, sidecarTable, groupColumn);
}
