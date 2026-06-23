using System.Data.Common;
using System.Globalization;
using Dapper;
using Ferret.Abstractions;
using Ferret.Core.Backends.Hybrid;
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
public class HybridSearchEndToEndTests
{
    private readonly PgVectorFixture _fx;

    public HybridSearchEndToEndTests(PgVectorFixture fx) => _fx = fx;

    // Vector property is a STRING; its embedding is derived from the text via FakeEmbeddingProvider.
    // FT matches on Title text; Vector matches on the Body embedding. Both live in group "content".
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

    // Variant: strict vector confidence threshold so weak (distant) vector matches self-eliminate.
    [SearchableEntity(Table = "hdocs")]
    [SearchGroup("content", FullTextConfig = "english")]
    [HybridBackend(SearchBackend.Vector, ConfidenceThreshold = 0.01)]
    public sealed class HDocStrict : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }

        [Searchable(Backend = SearchBackend.FullText, Group = "content")]
        public string Title { get; init; } = "";

        [Searchable(Backend = SearchBackend.Vector, Group = "content", EmbeddingDimensions = 8)]
        public string Body { get; init; } = "";
    }

    // Variant: heavy vector weight so a vector-favoured doc climbs the fused order.
    [SearchableEntity(Table = "hdocs")]
    [SearchGroup("content", FullTextConfig = "english")]
    [HybridBackend(SearchBackend.Vector, Weight = 10)]
    public sealed class HDocVecBoost : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }

        [Searchable(Backend = SearchBackend.FullText, Group = "content")]
        public string Title { get; init; } = "";

        [Searchable(Backend = SearchBackend.Vector, Group = "content", EmbeddingDimensions = 8)]
        public string Body { get; init; } = "";
    }

    [SkippableFact]
    public async Task Rrf_consensus_ranks_doc_matched_by_both_backends_first()
    {
        var provider = new FakeEmbeddingProvider(8);
        const string term = "reindexing";

        // 'both' body == term → vector distance 0 → vector rank 1 (unique: vecOnly has a different body).
        // This ensures 'both' wins rank 1 in the vector arm deterministically; vecOnly can only rank ≥2.
        var both    = (Id: Guid.NewGuid(), Title: "reindexing patterns",        Body: term);
        var ftOnly  = (Id: Guid.NewGuid(), Title: "reindexing strategies",       Body: "one two three four five six seven");
        var vecOnly = (Id: Guid.NewGuid(), Title: "completely unrelated heading", Body: "indexing and reindexing documents");

        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await SeedAsync(conn, provider, new[] { both, ftOnly, vecOnly });

        await using var sp = BuildServices<HDoc>(provider, efSearch: 40);
        var result = await SearchAsync<HDoc>(sp, term, limit: 10);

        result.Items.Should().NotBeEmpty();
        result.Items[0].Id.Should().Be(both.Id,
            "the doc both backends rank highly wins via summed RRF (consensus), ahead of either solo hit");
    }

    [SkippableFact]
    public async Task Ef_search_caps_vector_arm_in_the_fused_query()
    {
        // GATE 2 (non-negotiable): proves the vector CTE inside the FUSED query still uses the
        // HNSW index + SET LOCAL hnsw.ef_search. pgvector's HNSW scan returns at most ef_search
        // candidates, so with ef_search=1 the vector arm contributes ~1 row and the fused union
        // shrinks below LIMIT; with ef_search=1000 it returns the full page. If SET LOCAL never
        // reached the fused SELECT both runs would return the full LIMIT — the gap is the proof.
        var provider = new FakeEmbeddingProvider(8);

        const int n = 1000;
        var rows = Enumerable.Range(0, n)
            .Select(i => (Id: Guid.NewGuid(),
                          Title: $"zzqxnomatch heading {i}",            // no FT match for the search term
                          Body: $"document number {i} content alpha{i}beta"))
            .ToArray();

        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await SeedAsync(conn, provider, rows);

        const int limit = 20;
        // Search term has NO full-text match (FT arm empty) so the fused union is driven purely by
        // the vector arm — isolating the ef_search row-count signal.
        var term = rows[500].Body;

        await using var spLow  = BuildServices<HDoc>(provider, efSearch: 1);
        await using var spHigh = BuildServices<HDoc>(provider, efSearch: 1000);

        var low  = await SearchAsync<HDoc>(spLow,  term, limit);
        var high = await SearchAsync<HDoc>(spHigh, term, limit);

        high.Items.Should().HaveCount(limit,
            "ef_search=1000 >= LIMIT so the HNSW scan yields the full page through the fused query");
        low.Items.Count.Should().BeLessThan(high.Items.Count,
            "ef_search=1 caps the vector CTE's candidate list below LIMIT={0}, shrinking the fused union — proving SET LOCAL hnsw.ef_search reached the fused SELECT and the vector CTE uses HNSW", limit);
    }

    [SkippableFact]
    public async Task Strict_confidence_threshold_drops_weak_vector_match()
    {
        var provider = new FakeEmbeddingProvider(8);
        const string term = "reindexing";

        // ftOnly: FT match on title, body far from the query embedding.
        // vecOnly: NO FT match, body also far from the query (distance > 0.01 threshold).
        // With the strict 0.01 vector threshold the vector arm drops vecOnly's distant body, and
        // since it also has no FT match it vanishes from the fused result. ftOnly survives via FT.
        var ftOnly  = (Id: Guid.NewGuid(), Title: "reindexing strategies",        Body: "one two three four five six seven");
        var vecOnly = (Id: Guid.NewGuid(), Title: "completely unrelated heading", Body: "alpha bravo charlie delta echo foxtrot");

        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await SeedAsync(conn, provider, new[] { ftOnly, vecOnly });

        await using var sp = BuildServices<HDocStrict>(provider, efSearch: 40);
        var result = await SearchAsync<HDocStrict>(sp, term, limit: 10);

        result.Items.Select(x => x.Id).Should().Contain(ftOnly.Id,
            "the full-text match still surfaces despite the strict vector threshold");
        result.Items.Select(x => x.Id).Should().NotContain(vecOnly.Id,
            "a doc that only matches the vector arm weakly (distance above the threshold) self-eliminates");
    }

    [SkippableFact]
    public async Task Vector_weight_lifts_a_vector_favoured_doc()
    {
        var provider = new FakeEmbeddingProvider(8);
        const string term = "reindexing";

        // Each doc participates in exactly ONE arm so the arms are cleanly separated:
        //   vecDoc: body == term (vector rank 1), title has no FT match, NO vector row omitted -> vector-only.
        //   ftDoc : title has the term (FT rank 1), NO embedding (vector row omitted) -> full-text-only.
        // Equal weights (HDoc): vecDoc=1/(k+1), ftDoc=1/(k+1) -> tie. Vector Weight=10 (HDocVecBoost):
        // vecDoc=10/(k+1) strictly dominates ftDoc=1/(k+1), so vecDoc must rank first only when boosted.
        var vecDoc = (Id: Guid.NewGuid(), Title: "completely unrelated heading", Body: term,                  Embed: true);
        var ftDoc  = (Id: Guid.NewGuid(), Title: "reindexing strategies",        Body: "no embedding present", Embed: false);

        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await SeedAsync(conn, provider, new[] { vecDoc, ftDoc });

        await using var spBoost = BuildServices<HDocVecBoost>(provider, efSearch: 40);
        var boosted = await SearchAsync<HDocVecBoost>(spBoost, term, limit: 10);

        boosted.Items.Should().HaveCount(2);
        boosted.Items[0].Id.Should().Be(vecDoc.Id,
            "vector Weight=10 makes the vector-only doc's RRF contribution (10/(k+1)) dominate the full-text-only doc (1/(k+1))");
    }

    private static ServiceProvider BuildServices<T>(FakeEmbeddingProvider provider, int efSearch)
        where T : class
    {
        var sc = new ServiceCollection();
        sc.AddLogging();
        sc.AddFerret(o => o.ScanAssembly(typeof(T).Assembly)
            .UseFullTextSearch(ft => ft.DefaultConfig = "english")
            .UseVectorSearch(v => { v.UseEmbeddingProvider(_ => provider); v.EfSearch = efSearch; })
            .UseHybridSearch(_ => { })
            .UseDapperHydration());
        return sc.BuildServiceProvider();
    }

    private async Task<OffsetResult<T>> SearchAsync<T>(ServiceProvider sp, string term, int limit)
        where T : class
    {
        var engine = sp.GetRequiredService<IFerretEngine>();
        var dialect = sp.GetRequiredService<ISqlDialect>();
        var csb = new NpgsqlConnectionStringBuilder(_fx.ConnectionString) { PersistSecurityInfo = true };
        await using var session = new DapperSession(
            ct => Task.FromResult<DbConnection>(new NpgsqlConnection(csb.ConnectionString)),
            dialect);
        return await engine.SearchOffsetAsync<T, Guid>(session,
            new PagedQuery<T, Guid> { Mode = PaginationMode.Offset, Search = term, Limit = limit });
    }

    private static Task SeedAsync(
        NpgsqlConnection conn,
        FakeEmbeddingProvider provider,
        IReadOnlyList<(Guid Id, string Title, string Body)> rows) =>
        SeedAsync(conn, provider, rows.Select(r => (r.Id, r.Title, r.Body, Embed: true)).ToList());

    private static async Task SeedAsync(
        NpgsqlConnection conn,
        FakeEmbeddingProvider provider,
        IReadOnlyList<(Guid Id, string Title, string Body, bool Embed)> rows)
    {
        var sidecar = VectorSidecarNaming.TableName("hdocs", new VectorOptions());
        var col     = VectorSidecarNaming.ColumnName("content", new VectorOptions(), VectorSidecarNaming.CurrentVersion);
        var idx     = VectorSidecarNaming.IndexName(sidecar, col);

        // Base table + FT sidecar (content_tsv from title) + trigger, hand-written per the FullText template.
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
