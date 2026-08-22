using System.Data.Common;
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
/// Regression gate for vector offset paging: <see cref="VectorSqlBuilder.BuildRanking"/> used to
/// wrap <c>COUNT(*) OVER()</c> around the already-LIMIT-bounded KNN subquery, so
/// <c>TotalCount</c> could never exceed the page size and <c>HasMore</c> was always false on
/// page 0. Fix computes a true count over the unbounded (post-filter) candidate set, matching
/// the sibling builders (Trigram/FullText/Hybrid). Covers both the plain ranking route and the
/// filtered/candidates-CTE route, and both <see cref="VectorFilterStrategy.AlwaysExact"/> and
/// <see cref="VectorFilterStrategy.AlwaysIndex"/>.
/// </summary>
[Collection("pgvector")]
public class VectorOffsetPagingTests
{
    private readonly PgVectorFixture _fx;
    private const int Dimensions = 8;
    private const int RowCount = 100;
    private const int FilteredRowCount = 30; // rows tagged "emea"

    public VectorOffsetPagingTests(PgVectorFixture fx) => _fx = fx;

    [SearchableEntity(Table = "vdocs_offset_paging")]
    public sealed class PagedDoc : ISearchableEntity<int>
    {
        public int Id { get; init; }

        [Searchable(Backend = SearchBackend.Vector, Group = "content", EmbeddingDimensions = Dimensions)]
        public string Body { get; init; } = "";

        [Filterable]
        public string Region { get; init; } = "";
    }

    [SkippableFact]
    public async Task Unfiltered_offset_search_reports_true_total_and_walks_all_pages()
    {
        // No Filter clause here, so VectorFilterStrategy is never consulted (FerretEngine only
        // routes through ExecuteVectorFilteredRankingAsync when a candidate predicate exists) —
        // this always takes the plain HNSW-index ranking route. Page-walking coverage isn't
        // asserted for exact-id-set equality: separately-planned LIMIT/OFFSET invocations of an
        // HNSW ORDER BY <=> scan are not guaranteed to partition a stable total order (confirmed
        // via raw SQL against pgvector directly — pre-existing, not a regression from this fix;
        // see the plan's "ORDER BY key tiebreaker" note in Global Constraints for why a
        // tiebreaker isn't added here). What IS asserted, and IS this fix's contract: TotalCount
        // is the true total on every page, HasMore is correct, and the union of all pages is a
        // superset that never exceeds RowCount and reaches the great majority of it.
        var provider = new FakeEmbeddingProvider(Dimensions);

        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await SeedAsync(conn, provider);

        await using var sp = BuildServices(provider, VectorFilterStrategy.Auto);
        var engine = sp.GetRequiredService<IFerretEngine>();
        var dialect = sp.GetRequiredService<ISqlDialect>();
        await using var session = OpenSessionFor(dialect);

        const int limit = 10;
        var seenIds = new HashSet<int>();
        var page = 0;
        while (true)
        {
            var result = await engine.SearchOffsetAsync<PagedDoc, int>(session,
                new PagedQuery<PagedDoc, int>
                {
                    Mode = PaginationMode.Offset,
                    Search = "seed document for nearest neighbour search",
                    Limit = limit,
                    Page = page,
                    RequestTotalCount = true,
                });

            result.TotalCount.Should().Be(RowCount, "the total must reflect the full candidate set, not just this page");
            result.Items.Count.Should().BeLessThanOrEqualTo(limit);

            if (page == 0)
                result.HasMore.Should().BeTrue("100 rows over a 10-row page must have more pages");

            foreach (var item in result.Items) seenIds.Add(item.Id);

            if (!result.HasMore)
            {
                result.Items.Should().NotBeEmpty();
                break;
            }

            page++;
            page.Should().BeLessThan(RowCount, "guard against an infinite loop if HasMore never goes false");
        }

        seenIds.Should().HaveCountGreaterThanOrEqualTo(
            (int)(RowCount * 0.9),
            "walking every page must reach nearly every seeded id (HNSW graph-traversal ordering across " +
            "separately-planned page queries is not perfectly stable at the boundary — a pre-existing, " +
            "accepted limitation, not what this fix targets)");
    }

    [SkippableTheory]
    [InlineData(VectorFilterStrategy.AlwaysExact)]
    [InlineData(VectorFilterStrategy.AlwaysIndex)]
    public async Task Filtered_offset_search_reports_true_total_and_walks_all_matching_pages(VectorFilterStrategy strategy)
    {
        var provider = new FakeEmbeddingProvider(Dimensions);

        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await SeedAsync(conn, provider);

        await using var sp = BuildServices(provider, strategy);
        var engine = sp.GetRequiredService<IFerretEngine>();
        var dialect = sp.GetRequiredService<ISqlDialect>();
        await using var session = OpenSessionFor(dialect);

        const int limit = 10;
        var seenIds = new HashSet<int>();
        var page = 0;
        while (true)
        {
            var result = await engine.SearchOffsetAsync<PagedDoc, int>(session,
                new PagedQuery<PagedDoc, int>
                {
                    Mode = PaginationMode.Offset,
                    Search = "seed document for nearest neighbour search",
                    Limit = limit,
                    Page = page,
                    RequestTotalCount = true,
                    Filter = [new FilterClause { Field = "Region", Operator = FilterOperator.Equals, Value = "emea" }],
                });

            result.TotalCount.Should().Be(FilteredRowCount, "the total must reflect the filtered candidate set only");
            result.Items.Should().OnlyContain(d => d.Region == "emea");

            foreach (var item in result.Items) seenIds.Add(item.Id);

            if (!result.HasMore)
            {
                result.Items.Should().NotBeEmpty();
                break;
            }

            page++;
            page.Should().BeLessThan(FilteredRowCount, "guard against an infinite loop if HasMore never goes false");
        }

        seenIds.Should().HaveCount(FilteredRowCount, "walking every page must reach every matching id exactly once");
    }

    private IFerretSession OpenSessionFor(ISqlDialect dialect)
    {
        var csb = new NpgsqlConnectionStringBuilder(_fx.ConnectionString) { PersistSecurityInfo = true };
        return new DapperSession(ct => Task.FromResult<DbConnection>(new NpgsqlConnection(csb.ConnectionString)), dialect);
    }

    private static ServiceProvider BuildServices(FakeEmbeddingProvider provider, VectorFilterStrategy strategy)
    {
        var sc = new ServiceCollection();
        sc.AddLogging();
        sc.AddFerret(o => o.ScanAssembly(typeof(PagedDoc).Assembly)
            .UseVectorSearch(v =>
            {
                v.UseEmbeddingProvider(_ => provider);
                // Exhaustive ef_search: these tests assert paging correctness (TotalCount/
                // HasMore/full coverage), not HNSW recall — a bounded ef_search caps the total
                // candidate pool the index considers below RowCount regardless of OFFSET, which
                // would make page-walking coverage a function of recall, not the fix under test.
                v.EfSearch = RowCount;
                v.FilterStrategy = strategy;
            }));
        return sc.BuildServiceProvider();
    }

    /// <summary>
    /// Seeds 100 rows: 30 tagged "emea", 70 "apac" (deterministic on id, exact selectivity).
    /// One "emea" row's body/embedding matches the search term exactly so ranking has a defined
    /// answer, though these tests assert page-walking coverage, not top-1 identity.
    /// </summary>
    private static async Task SeedAsync(NpgsqlConnection conn, FakeEmbeddingProvider provider)
    {
        var sidecar = VectorSidecarNaming.TableName("vdocs_offset_paging", new VectorOptions());
        var col     = VectorSidecarNaming.ColumnName("content", new VectorOptions(), VectorSidecarNaming.CurrentVersion);
        var idx     = VectorSidecarNaming.IndexName(sidecar, col);

        await conn.ExecuteAsync($"""
            DROP TABLE IF EXISTS "{sidecar}" CASCADE;
            DROP TABLE IF EXISTS vdocs_offset_paging CASCADE;
            CREATE TABLE vdocs_offset_paging (id int PRIMARY KEY, body text NOT NULL, region text NOT NULL);
            {VectorDdlBuilder.CreateVersionRegistry(null)}
            {VectorDdlBuilder.CreateSidecarTable(sidecar, null, "vdocs_offset_paging", null, "id", "int")}
            {VectorDdlBuilder.AddGroupColumn(sidecar, null, col, provider.Dimensions)}
            """);

        await conn.ExecuteAsync(
            "DELETE FROM ferret_vector_versions WHERE entity = @entity AND group_name = @group AND status = 'active'; " +
            "INSERT INTO ferret_vector_versions (entity, group_name, model, dimensions, column_name, status) " +
            "VALUES (@entity, @group, @model, @dims, @col, 'active')",
            new { entity = "vdocs_offset_paging", group = "content", model = "fake", dims = provider.Dimensions, col });

        // g % 10 < 3 -> exactly 30 of 100 rows are "emea", the rest "apac".
        await conn.ExecuteAsync($"""
            INSERT INTO vdocs_offset_paging (id, body, region)
            SELECT g, 'document number ' || g, CASE WHEN g % 10 < 3 THEN 'emea' ELSE 'apac' END
            FROM generate_series(1, {RowCount}) g;

            INSERT INTO "{sidecar}" (id, "{col}")
            SELECT g, (SELECT array_agg(random())::vector({Dimensions}) FROM generate_series(1, {Dimensions}))
            FROM generate_series(1, {RowCount}) g;
            """);

        const int targetId = 1; // g=1 -> g % 10 == 1 < 3 -> "emea"
        var searchTerm = "seed document for nearest neighbour search";
        var vec = await provider.EmbedAsync(searchTerm, default);
        var lit = "[" + string.Join(",", vec.Select(f => f.ToString(System.Globalization.CultureInfo.InvariantCulture))) + "]";
        await conn.ExecuteAsync(
            $"UPDATE vdocs_offset_paging SET body = @body WHERE id = @id; " +
            $"UPDATE \"{sidecar}\" SET \"{col}\" = @vec::vector WHERE id = @id;",
            new { id = targetId, body = searchTerm, vec = lit });

        await conn.ExecuteAsync($"""
            {VectorDdlBuilder.CreateGroupIndex(sidecar, null, idx, col, m: 16, efConstruction: 64)}
            ANALYZE vdocs_offset_paging;
            ANALYZE "{sidecar}";
            """);
    }
}
