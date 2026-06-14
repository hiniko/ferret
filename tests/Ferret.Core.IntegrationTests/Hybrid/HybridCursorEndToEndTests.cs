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

namespace Ferret.Core.IntegrationTests.Hybrid;

[Collection("pgvector")]
public class HybridCursorEndToEndTests
{
    private readonly PgVectorFixture _fx;

    public HybridCursorEndToEndTests(PgVectorFixture fx) => _fx = fx;

    [SearchableEntity(Table = "hdocs")]
    [SearchGroup("content", FullTextConfig = "english")]
    public sealed class HDoc : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }

        [Searchable(Backend = SearchBackend.FullText, Group = "content")]
        public string Title { get; init; } = "";

        [Searchable(Backend = SearchBackend.Vector, Group = "content", EmbeddingDimensions = 8)]
        public string Body { get; init; } = "";
    }

    [SkippableFact]
    public async Task Cursor_pages_advance_disjointly_over_the_fused_query()
    {
        BenchGate.SkipUnlessEnabled();

        var provider = new FakeEmbeddingProvider(8);
        const string term = "reindexing";

        // 12 docs. The FT arm matches all titles (term in title). The vector arm matches the
        // subset whose body == term (distance 0). So the fused set has 12 rows ordered by RRF.
        var rows = Enumerable.Range(0, 12).Select(i => (
            Id: Guid.NewGuid(),
            Title: $"reindexing patterns number {i:D2}",
            Body: i % 2 == 0 ? term : $"unrelated body filler number {i}",
            Embed: true)).ToArray();

        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await SeedAsync(conn, provider, rows);

        await using var sp = BuildServices(provider);
        var (engine, session) = Open(sp);
        await using var _ = session;

        var page1 = await engine.SearchCursorAsync<HDoc, Guid>(session, new PagedQuery<HDoc, Guid>
        {
            Mode = PaginationMode.Cursor,
            Search = term,
            Limit = 5,
        });

        page1.Items.Should().HaveCount(5);
        page1.NextCursor.Should().NotBeNull();
        page1.HasMore.Should().BeTrue();

        var page2 = await engine.SearchCursorAsync<HDoc, Guid>(session, new PagedQuery<HDoc, Guid>
        {
            Mode = PaginationMode.Cursor,
            Search = term,
            Limit = 5,
            Cursor = page1.NextCursor,
            CursorDirection = CursorDirection.Forward,
        });

        page2.Items.Should().HaveCount(5);
        page2.Items.Select(d => d.Id).Should().NotIntersectWith(page1.Items.Select(d => d.Id),
            "page 2 is the next-ranked slice of the fused result, disjoint from page 1");
        page2.NextCursor.Should().NotBeNull();

        var page3 = await engine.SearchCursorAsync<HDoc, Guid>(session, new PagedQuery<HDoc, Guid>
        {
            Mode = PaginationMode.Cursor,
            Search = term,
            Limit = 5,
            Cursor = page2.NextCursor,
            CursorDirection = CursorDirection.Forward,
        });

        page3.Items.Should().HaveCount(2);
        page3.Items.Select(d => d.Id).Should().NotIntersectWith(page1.Items.Select(d => d.Id));
        page3.Items.Select(d => d.Id).Should().NotIntersectWith(page2.Items.Select(d => d.Id));
        page3.NextCursor.Should().BeNull();
        page3.HasMore.Should().BeFalse();

        var allIds = page1.Items.Concat(page2.Items).Concat(page3.Items).Select(d => d.Id).ToList();
        allIds.Should().OnlyHaveUniqueItems();
        allIds.Should().HaveCount(12, "the fused result is fully and disjointly paged over the cursor");
    }

    private (IFerretEngine Engine, DapperSession Session) Open(ServiceProvider sp)
    {
        var engine = sp.GetRequiredService<IFerretEngine>();
        var dialect = sp.GetRequiredService<ISqlDialect>();
        var csb = new NpgsqlConnectionStringBuilder(_fx.ConnectionString) { PersistSecurityInfo = true };
        var session = new DapperSession(
            ct => Task.FromResult<DbConnection>(new NpgsqlConnection(csb.ConnectionString)),
            dialect);
        return (engine, session);
    }

    private static ServiceProvider BuildServices(FakeEmbeddingProvider provider)
    {
        var sc = new ServiceCollection();
        sc.AddLogging();
        sc.AddFerret(o => o.ScanAssembly(typeof(HDoc).Assembly).UsePostgres()
            .UseFullTextSearch(ft => ft.DefaultConfig = "english")
            .UseVectorSearch(v => { v.UseEmbeddingProvider(_ => provider); v.EfSearch = 40; })
            .UseHybridSearch(_ => { })
            .UseDapperHydration());
        return sc.BuildServiceProvider();
    }

    private static async Task SeedAsync(
        NpgsqlConnection conn,
        FakeEmbeddingProvider provider,
        IReadOnlyList<(Guid Id, string Title, string Body, bool Embed)> rows)
    {
        var sidecar = VectorSidecarNaming.TableName("hdocs", new VectorOptions());
        var col     = VectorSidecarNaming.ColumnName("content", new VectorOptions(), VectorSidecarNaming.CurrentVersion);
        var idx     = VectorSidecarNaming.IndexName(sidecar, col);

        await conn.ExecuteAsync($"""
            DROP TABLE IF EXISTS "{sidecar}" CASCADE;
            DROP TABLE IF EXISTS hdocs_search CASCADE;
            DROP TABLE IF EXISTS hdocs CASCADE;
            CREATE TABLE hdocs (id uuid PRIMARY KEY, title text NOT NULL, body text NOT NULL);

            CREATE TABLE hdocs_search (
                id uuid PRIMARY KEY REFERENCES hdocs(id) ON DELETE CASCADE,
                content_tsv tsvector,
                updated_at timestamptz NOT NULL DEFAULT now()
            );
            CREATE INDEX ix_hdocs_search_content_tsv_gin ON hdocs_search USING gin (content_tsv);

            CREATE OR REPLACE FUNCTION hdocs_search_sync() RETURNS trigger AS $$
            BEGIN
                INSERT INTO hdocs_search (id, content_tsv, updated_at)
                VALUES (NEW.id,
                    setweight(to_tsvector('english', coalesce(NEW.title, '')), 'A'),
                    now())
                ON CONFLICT (id) DO UPDATE
                SET content_tsv = EXCLUDED.content_tsv, updated_at = now();
                RETURN NEW;
            END $$ LANGUAGE plpgsql;

            CREATE TRIGGER hdocs_search_sync_t
                AFTER INSERT OR UPDATE OF title ON hdocs
                FOR EACH ROW EXECUTE FUNCTION hdocs_search_sync();

            {VectorDdlBuilder.CreateVersionRegistry(null)}
            {VectorDdlBuilder.CreateSidecarTable(sidecar, null, "hdocs", null, "id", "uuid")}
            {VectorDdlBuilder.AddGroupColumn(sidecar, null, col, provider.Dimensions)}
            {VectorDdlBuilder.CreateGroupIndex(sidecar, null, idx, col, m: 16, efConstruction: 64)}
            """);

        await conn.ExecuteAsync(
            "DELETE FROM ferret_vector_versions WHERE entity = @entity AND group_name = @group AND status = 'active'; " +
            "INSERT INTO ferret_vector_versions (entity, group_name, model, dimensions, column_name, status) " +
            "VALUES (@entity, @group, @model, @dims, @col, 'active')",
            new { entity = "hdocs", group = "content", model = "fake", dims = provider.Dimensions, col });

        foreach (var (id, title, body, embed) in rows)
        {
            await conn.ExecuteAsync(
                "INSERT INTO hdocs (id, title, body) VALUES (@id, @title, @body);",
                new { id, title, body });
            if (!embed) continue;
            var vec = await provider.EmbedAsync(body, default);
            var lit = "[" + string.Join(",", vec.Select(f => f.ToString(CultureInfo.InvariantCulture))) + "]";
            await conn.ExecuteAsync(
                $"INSERT INTO \"{sidecar}\" (id, \"{col}\") VALUES (@id, @vec::vector);",
                new { id, vec = lit });
        }
    }
}
