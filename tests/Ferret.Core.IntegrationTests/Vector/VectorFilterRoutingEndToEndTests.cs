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

/// <summary>
/// Live gate for selectivity-based filter routing (docs/vector-filtering.md,
/// docs/superpowers/specs/2026-08-22-filtered-vector-search.md). HNSW is approximate and
/// cannot see a WHERE clause, so a selective filter combined with the index silently returns a
/// short page. These tests seed enough rows that the bug is actually visible (the 3-row
/// fixtures elsewhere in this file prove correctness, not recall) and assert the fix.
/// </summary>
[Collection("pgvector")]
public class VectorFilterRoutingEndToEndTests
{
    private readonly PgVectorFixture _fx;
    private const int Dimensions = 8;
    private const int RowCount = 50_000;
    // "emea" is assigned to 1 row in 100 (~1% selectivity) — matches the spec's measured scenario.
    private const int SelectiveModulus = 100;

    public VectorFilterRoutingEndToEndTests(PgVectorFixture fx) => _fx = fx;

    [SearchableEntity(Table = "vdocs_routing")]
    public sealed class RoutedDoc : ISearchableEntity<int>
    {
        public int Id { get; init; }

        [Searchable(Backend = SearchBackend.Vector, Group = "content", EmbeddingDimensions = Dimensions)]
        public string Body { get; init; } = "";

        [Filterable]
        public string Region { get; init; } = "";
    }

    [SkippableFact]
    public async Task Selective_filter_returns_full_page_via_exact_scan()
    {
        // THE regression gate: 50k rows, ~1% selective filter, LIMIT 25. Ferret's pre-routing
        // behaviour ranks via the HNSW index and applies the filter afterwards, so the index's
        // ~ef_search candidates get filtered down to a handful — measured 4-6 rows in the spec.
        // With Auto routing and a threshold above the estimated ~500-row filtered set, this must
        // route to an exact scan and return the full 25.
        var provider = new FakeEmbeddingProvider(Dimensions);

        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await SeedRoutingTableAsync(conn, provider);

        await using var sp = BuildServices(provider, v =>
        {
            v.EfSearch = 40; // matches the spec's measured ef_search
            v.FilterStrategy = VectorFilterStrategy.Auto;
            v.ExactScanThreshold = 10_000; // ~500 estimated rows is well under this
        });
        var engine = sp.GetRequiredService<IFerretEngine>();
        var dialect = sp.GetRequiredService<ISqlDialect>();

        await using var session = OpenSessionFor(dialect);

        const int limit = 25;
        var result = await engine.SearchOffsetAsync<RoutedDoc, int>(session,
            new PagedQuery<RoutedDoc, int>
            {
                Mode = PaginationMode.Offset,
                Search = "seed document for nearest neighbour search",
                Limit = limit,
                Filter = [new FilterClause { Field = "Region", Operator = FilterOperator.Equals, Value = "emea" }],
            });

        result.Items.Should().HaveCount(limit,
            "a ~1% selective filter over {0} rows must return the full requested page via the exact-scan path, " +
            "not a short page truncated by the HNSW index (see docs/vector-filtering.md)", RowCount);
        result.Items.Should().OnlyContain(d => d.Region == "emea");
    }

    [SkippableFact]
    public async Task AlwaysIndex_strategy_reproduces_the_short_page_bug()
    {
        // Proves the new live gate actually exercises the pre-fix code path: forcing the old
        // "always HNSW" behaviour on the same 50k-row/1%-selective dataset reproduces the bug
        // this change fixes. This is the "fails before, passes after" evidence — AlwaysIndex is
        // exactly today's (pre-routing) behaviour.
        var provider = new FakeEmbeddingProvider(Dimensions);

        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await SeedRoutingTableAsync(conn, provider);

        await using var sp = BuildServices(provider, v =>
        {
            v.EfSearch = 40;
            v.FilterStrategy = VectorFilterStrategy.AlwaysIndex;
        });
        var engine = sp.GetRequiredService<IFerretEngine>();
        var dialect = sp.GetRequiredService<ISqlDialect>();
        await using var session = OpenSessionFor(dialect);

        const int limit = 25;
        var result = await engine.SearchOffsetAsync<RoutedDoc, int>(session,
            new PagedQuery<RoutedDoc, int>
            {
                Mode = PaginationMode.Offset,
                Search = "seed document for nearest neighbour search",
                Limit = limit,
                Filter = [new FilterClause { Field = "Region", Operator = FilterOperator.Equals, Value = "emea" }],
            });

        result.Items.Count.Should().BeLessThan(limit,
            "AlwaysIndex reproduces the pre-fix behaviour: HNSW cannot see the WHERE clause, so a " +
            "~1% selective filter over {0} rows returns far fewer than the requested {1}", RowCount, limit);
    }

    [SkippableFact]
    public async Task Mild_filter_still_takes_the_index_path()
    {
        // Acceptance criterion #3: a ~90%-selective filter must still use the index, not fall
        // into an exact scan over most of the table. Verified via EXPLAIN on the same shape of
        // query the engine builds (base-table CTE join over the sidecar).
        var provider = new FakeEmbeddingProvider(Dimensions);

        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await SeedRoutingTableAsync(conn, provider);

        // "apac" covers ~99% of rows (the complement of the 1%-selective "emea" filter above).
        var plan = await ExplainFilteredKnnAsync(conn, region: "apac", disableIndexScan: false);
        plan.Should().Contain("Index Scan", "a mild (~99%-selective) filter must still use the HNSW index");
    }

    [SkippableFact]
    public async Task Selective_filter_plan_does_not_use_the_index_when_disabled()
    {
        // Confirms the mechanism the exact-scan path relies on: with enable_indexscan/
        // enable_indexonlyscan off, Postgres genuinely cannot use the HNSW index, regardless of
        // filter selectivity.
        var provider = new FakeEmbeddingProvider(Dimensions);

        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await SeedRoutingTableAsync(conn, provider);

        var plan = await ExplainFilteredKnnAsync(conn, region: "emea", disableIndexScan: true);
        plan.Should().NotContain("Index Scan using", "enable_indexscan/enable_indexonlyscan = off must force a non-index plan");
    }

    private static async Task<string> ExplainFilteredKnnAsync(NpgsqlConnection conn, string region, bool disableIndexScan)
    {
        var sidecar = VectorSidecarNaming.TableName("vdocs_routing", new VectorOptions());
        var col = VectorSidecarNaming.ColumnName("content", new VectorOptions(), VectorSidecarNaming.CurrentVersion);

        await using var tx = await conn.BeginTransactionAsync();
        if (disableIndexScan)
        {
            await conn.ExecuteAsync("SET LOCAL enable_indexscan = off; SET LOCAL enable_indexonlyscan = off;", transaction: tx);
        }

        var qvec = "[" + string.Join(",", Enumerable.Repeat(0.1f, Dimensions).Select(f => f.ToString(CultureInfo.InvariantCulture))) + "]";
        var sql = $"""
            EXPLAIN (FORMAT TEXT)
            WITH candidates_ids AS (SELECT e.id FROM vdocs_routing e WHERE e.region = @region)
            SELECT knn.id FROM (
                SELECT s.id
                FROM "{sidecar}" s
                WHERE s."{col}" IS NOT NULL
                  AND EXISTS (SELECT 1 FROM candidates_ids c WHERE c.id = s.id)
                ORDER BY s."{col}" <=> (@qvec)::vector
                LIMIT 25
            ) knn
            """;
        var rows = await conn.QueryAsync<string>(sql, new { region, qvec }, transaction: tx);
        await tx.RollbackAsync();
        return string.Join("\n", rows);
    }

    private IFerretSession OpenSessionFor(ISqlDialect dialect)
    {
        var csb = new NpgsqlConnectionStringBuilder(_fx.ConnectionString) { PersistSecurityInfo = true };
        return new DapperSession(ct => Task.FromResult<DbConnection>(new NpgsqlConnection(csb.ConnectionString)), dialect);
    }

    private static ServiceProvider BuildServices(FakeEmbeddingProvider provider, Action<VectorOptions> configure)
    {
        var sc = new ServiceCollection();
        sc.AddLogging();
        sc.AddFerret(o => o.ScanAssembly(typeof(RoutedDoc).Assembly)
            .UseVectorSearch(v =>
            {
                v.UseEmbeddingProvider(_ => provider);
                configure(v);
            }));
        return sc.BuildServiceProvider();
    }

    /// <summary>
    /// Seeds 50k rows server-side (bulk, no per-row round trips): 1% tagged "emea", the rest
    /// "apac". Vectors are genuinely random per the spec's reproduction script, except for one
    /// row whose body/embedding matches the query text exactly so ranking has a defined answer.
    /// </summary>
    private static async Task SeedRoutingTableAsync(NpgsqlConnection conn, FakeEmbeddingProvider provider)
    {
        var sidecar = VectorSidecarNaming.TableName("vdocs_routing", new VectorOptions());
        var col     = VectorSidecarNaming.ColumnName("content", new VectorOptions(), VectorSidecarNaming.CurrentVersion);
        var idx     = VectorSidecarNaming.IndexName(sidecar, col);

        await conn.ExecuteAsync($"""
            DROP TABLE IF EXISTS "{sidecar}" CASCADE;
            DROP TABLE IF EXISTS vdocs_routing CASCADE;
            CREATE TABLE vdocs_routing (id int PRIMARY KEY, body text NOT NULL, region text NOT NULL);
            {VectorDdlBuilder.CreateVersionRegistry(null)}
            {VectorDdlBuilder.CreateSidecarTable(sidecar, null, "vdocs_routing", null, "id", "int")}
            {VectorDdlBuilder.AddGroupColumn(sidecar, null, col, provider.Dimensions)}
            """);

        await conn.ExecuteAsync(
            "DELETE FROM ferret_vector_versions WHERE entity = @entity AND group_name = @group AND status = 'active'; " +
            "INSERT INTO ferret_vector_versions (entity, group_name, model, dimensions, column_name, status) " +
            "VALUES (@entity, @group, @model, @dims, @col, 'active')",
            new { entity = "vdocs_routing", group = "content", model = "fake", dims = provider.Dimensions, col });

        // Bulk-seed random rows server-side (fast — no per-row round trips). Region is deterministic
        // on row id (1 in SelectiveModulus is "emea") so selectivity is exact, not probabilistic.
        await conn.ExecuteAsync($"""
            INSERT INTO vdocs_routing (id, body, region)
            SELECT g, 'document number ' || g, CASE WHEN g % {SelectiveModulus} = 0 THEN 'emea' ELSE 'apac' END
            FROM generate_series(1, {RowCount}) g;

            INSERT INTO "{sidecar}" (id, "{col}")
            SELECT g, (SELECT array_agg(random())::vector({Dimensions}) FROM generate_series(1, {Dimensions}))
            FROM generate_series(1, {RowCount}) g;
            """, commandTimeout: 120);

        // One "emea" row's body/embedding is overwritten to exactly match the search term, so
        // the nearest-neighbour answer is well-defined and lands inside the filtered set.
        const int targetId = SelectiveModulus; // g % 100 == 0 -> "emea"
        var searchTerm = "seed document for nearest neighbour search";
        var vec = await provider.EmbedAsync(searchTerm, default);
        var lit = "[" + string.Join(",", vec.Select(f => f.ToString(CultureInfo.InvariantCulture))) + "]";
        await conn.ExecuteAsync(
            $"UPDATE vdocs_routing SET body = @body WHERE id = @id; " +
            $"UPDATE \"{sidecar}\" SET \"{col}\" = @vec::vector WHERE id = @id;",
            new { id = targetId, body = searchTerm, vec = lit });

        await conn.ExecuteAsync($"""
            {VectorDdlBuilder.CreateGroupIndex(sidecar, null, idx, col, m: 16, efConstruction: 64)}
            ANALYZE vdocs_routing;
            ANALYZE "{sidecar}";
            """, commandTimeout: 120);
    }
}
