using System.Data.Common;
using System.Globalization;
using Dapper;
using Ferret.Abstractions;
using Ferret.Core.Backends.Vector;
using Ferret.Core.Embeddings;
using Ferret.Core.IntegrationTests.Fixtures;
using Ferret.Hydration.Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Ferret.Core.IntegrationTests.Vector;

[Collection("pgvector")]
public class VectorSearchEndToEndTests
{
    private readonly PgVectorFixture _fx;

    public VectorSearchEndToEndTests(PgVectorFixture fx) => _fx = fx;

    [SearchableEntity(Table = "vdocs")]
    public sealed class VDoc : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }

        [Searchable(Backend = SearchBackend.Vector, Group = "content", EmbeddingDimensions = 8)]
        public string Body { get; init; } = "";
    }

    [SkippableFact]
    public async Task Search_returns_nearest_neighbour_first()
    {
        BenchGate.SkipUnlessEnabled();

        var provider = new FakeEmbeddingProvider(8);

        var sentences = new[]
        {
            "The quick brown fox jumps over the lazy dog",
            "A stitch in time saves nine",
            "All that glitters is not gold",
            "To be or not to be that is the question",
            "Knowledge is power and power corrupts",
        };

        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await SeedAsync(conn, provider, sentences);

        await using var sp = BuildServices(provider, efSearch: 40);
        var engine = sp.GetRequiredService<IFerretEngine>();
        var dialect = sp.GetRequiredService<ISqlDialect>();

        var csb = new NpgsqlConnectionStringBuilder(_fx.ConnectionString) { PersistSecurityInfo = true };
        await using var session = new DapperSession(
            ct => Task.FromResult<DbConnection>(new NpgsqlConnection(csb.ConnectionString)),
            dialect);

        var target = sentences[2];
        var result = await engine.SearchOffsetAsync<VDoc, Guid>(session,
            new PagedQuery<VDoc, Guid> { Mode = PaginationMode.Offset, Search = target, Limit = 10 });

        result.Items.Should().NotBeEmpty();
        result.Items[0].Body.Should().Be(target, "the exact sentence's own embedding is the nearest neighbour to itself");
    }

    [SkippableFact]
    public async Task Recall_changes_with_ef_search()
    {
        BenchGate.SkipUnlessEnabled();

        // This test is the BLOCKER #2 GATE: it proves EfSearch reaches the
        // SET LOCAL hnsw.ef_search = N inside the transaction. pgvector's HNSW
        // scan returns at most ef_search candidates; when ef_search < LIMIT it
        // therefore returns FEWER than LIMIT rows. So with ef_search=1 the query
        // yields ~1 row and with ef_search=1000 it yields the full LIMIT. This
        // row-count gap is a deterministic, distribution-independent proof that
        // the session-local setting actually reaches the ranking SELECT (a value
        // that never reached the query would leave both runs at the full LIMIT).
        var provider = new FakeEmbeddingProvider(8);

        const int n = 1000;
        var bodies = Enumerable.Range(0, n)
            .Select(i => $"document number {i} with unique content identifier alpha{i}beta")
            .ToArray();

        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await SeedAsync(conn, provider, bodies);

        const int limit = 20;
        var searchTerm = bodies[500];

        await using var spLow  = BuildServices(provider, efSearch: 1);
        await using var spHigh = BuildServices(provider, efSearch: 1000);

        var engineLow  = spLow.GetRequiredService<IFerretEngine>();
        var engineHigh = spHigh.GetRequiredService<IFerretEngine>();

        var dialectLow  = spLow.GetRequiredService<ISqlDialect>();
        var dialectHigh = spHigh.GetRequiredService<ISqlDialect>();

        var csb = new NpgsqlConnectionStringBuilder(_fx.ConnectionString) { PersistSecurityInfo = true };

        await using var sessionLow = new DapperSession(
            ct => Task.FromResult<DbConnection>(new NpgsqlConnection(csb.ConnectionString)),
            dialectLow);

        await using var sessionHigh = new DapperSession(
            ct => Task.FromResult<DbConnection>(new NpgsqlConnection(csb.ConnectionString)),
            dialectHigh);

        var resultLow = await engineLow.SearchOffsetAsync<VDoc, Guid>(sessionLow,
            new PagedQuery<VDoc, Guid> { Mode = PaginationMode.Offset, Search = searchTerm, Limit = limit });

        var resultHigh = await engineHigh.SearchOffsetAsync<VDoc, Guid>(sessionHigh,
            new PagedQuery<VDoc, Guid> { Mode = PaginationMode.Offset, Search = searchTerm, Limit = limit });

        // ef_search=1000 (>= LIMIT) returns the full page, exact match ranked first.
        resultHigh.Items.Should().HaveCount(limit,
            "ef_search=1000 >= LIMIT so the HNSW scan yields the full page");
        resultHigh.Items[0].Body.Should().Be(searchTerm,
            "ef_search=1000 is exhaustive on a 1000-row dataset — exact match must rank first");

        // ef_search=1 caps the HNSW candidate list below LIMIT, so the query
        // returns FEWER than LIMIT rows. If the SET LOCAL never reached the SELECT
        // this run would also return the full LIMIT — the gap is the proof (blocker #2).
        resultLow.Items.Count.Should().BeLessThan(resultHigh.Items.Count,
            "ef_search=1 caps the candidate list below LIMIT={0}, so fewer rows come back — proving SET LOCAL hnsw.ef_search reached the ranking SELECT", limit);
    }

    private static ServiceProvider BuildServices(FakeEmbeddingProvider provider, int efSearch)
    {
        var sc = new ServiceCollection();
        sc.AddLogging();
        sc.AddFerret(o => o.ScanAssembly(typeof(VDoc).Assembly).UsePostgres()
            .UseVectorSearch(v => { v.UseEmbeddingProvider(_ => provider); v.EfSearch = efSearch; })
            .UseDapperHydration());
        return sc.BuildServiceProvider();
    }

    private static async Task SeedAsync(NpgsqlConnection conn, FakeEmbeddingProvider provider, string[] bodies)
    {
        var sidecar = VectorSidecarNaming.TableName("vdocs", new VectorOptions());
        var col     = VectorSidecarNaming.ColumnName("content", new VectorOptions(), VectorSidecarNaming.CurrentVersion);
        var idx     = VectorSidecarNaming.IndexName(sidecar, col);

        await conn.ExecuteAsync($"""
            DROP TABLE IF EXISTS "{sidecar}" CASCADE;
            DROP TABLE IF EXISTS vdocs CASCADE;
            CREATE TABLE vdocs (id uuid PRIMARY KEY, body text NOT NULL);
            {VectorDdlBuilder.CreateVersionRegistry(null)}
            {VectorDdlBuilder.CreateSidecarTable(sidecar, null, "vdocs", null, "id", "uuid")}
            {VectorDdlBuilder.AddGroupColumn(sidecar, null, col, provider.Dimensions)}
            {VectorDdlBuilder.CreateGroupIndex(sidecar, null, idx, col, m: 16, efConstruction: 64)}
            """);

        await conn.ExecuteAsync(
            "DELETE FROM ferret_vector_versions WHERE entity = @entity AND group_name = @group AND status = 'active'; " +
            "INSERT INTO ferret_vector_versions (entity, group_name, model, dimensions, column_name, status) " +
            "VALUES (@entity, @group, @model, @dims, @col, 'active')",
            new { entity = "vdocs", group = "content", model = "fake", dims = provider.Dimensions, col });

        foreach (var body in bodies)
        {
            var id  = Guid.NewGuid();
            var vec = await provider.EmbedAsync(body, default);
            var lit = "[" + string.Join(",", vec.Select(f => f.ToString(CultureInfo.InvariantCulture))) + "]";
            await conn.ExecuteAsync(
                $"INSERT INTO vdocs (id, body) VALUES (@id, @body); " +
                $"INSERT INTO \"{sidecar}\" (id, \"{col}\") VALUES (@id, @vec::vector);",
                new { id, body, vec = lit });
        }
    }
}
