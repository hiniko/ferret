using System.Data.Common;
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

[Collection("ollama+pgvector")]
public class OllamaReindexSearchGateTests
{
    private readonly OllamaFixture _ollama;
    private readonly PgVectorFixture _pg;

    public OllamaReindexSearchGateTests(OllamaFixture ollama, PgVectorFixture pg)
    {
        _ollama = ollama;
        _pg = pg;
    }

    [SearchableEntity(Table = "ovdocs")]
    public sealed class OvDoc : ISearchableEntity<long>
    {
        public long Id { get; init; }

        [Searchable(Backend = SearchBackend.Vector, Group = "content", EmbeddingDimensions = 768)]
        public string Body { get; init; } = "";
    }

    private ServiceProvider BuildServices() =>
        BuildServicesWithProvider(v => v.UseOllamaEmbeddings(_ollama.BaseAddress));

    private ServiceProvider BuildServicesWithProvider(Action<VectorOptions> configure)
    {
        var sc = new ServiceCollection();
        sc.AddLogging();
        sc.AddFerret(o => o
            .ScanAssembly(typeof(OvDoc).Assembly)
            .UseVectorSearch(configure)
            .UseDapperHydration());
        return sc.BuildServiceProvider();
    }

    private static DapperSession CreateSession(string connectionString, ISqlDialect dialect)
    {
        var csb = new NpgsqlConnectionStringBuilder(connectionString) { PersistSecurityInfo = true };
        return new DapperSession(
            ct => Task.FromResult<DbConnection>(new NpgsqlConnection(csb.ConnectionString)),
            dialect);
    }

    private static async Task ResetAndSeed(NpgsqlConnection conn, IEnumerable<(long Id, string Body)> rows)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            DROP TABLE IF EXISTS ovdocs_vec CASCADE;
            DROP TABLE IF EXISTS ovdocs CASCADE;
            CREATE TABLE ovdocs (
                id bigint PRIMARY KEY,
                body text NOT NULL
            );
            CREATE TABLE ovdocs_vec (
                id bigint PRIMARY KEY REFERENCES ovdocs(id) ON DELETE CASCADE,
                content_embedding_v1 vector(768),
                updated_at timestamptz NOT NULL DEFAULT now()
            );
            CREATE INDEX IF NOT EXISTS "ix_ovdocs_vec_content_embedding_v1_hnsw"
                ON ovdocs_vec USING hnsw (content_embedding_v1 vector_cosine_ops)
                WITH (m = 16, ef_construction = 64);
            CREATE TABLE IF NOT EXISTS ferret_vector_versions (
                version_id bigserial PRIMARY KEY,
                entity text NOT NULL,
                group_name text NOT NULL,
                model text NOT NULL,
                dimensions integer NOT NULL,
                column_name text NOT NULL,
                status text NOT NULL,
                created_at timestamptz NOT NULL DEFAULT now(),
                CONSTRAINT uq_ferret_vector_versions UNIQUE (entity, group_name, version_id)
            );
            CREATE UNIQUE INDEX IF NOT EXISTS uq_ferret_vector_versions_active
                ON ferret_vector_versions (entity, group_name) WHERE status = 'active';
            """;
        await cmd.ExecuteNonQueryAsync();

        foreach (var (id, body) in rows)
        {
            await using var seed = conn.CreateCommand();
            seed.CommandText = "INSERT INTO ovdocs (id, body) VALUES (@id, @body);";
            seed.Parameters.AddWithValue("@id", id);
            seed.Parameters.AddWithValue("@body", body);
            await seed.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Reindex_fills_versioned_column_and_stamps_active_registry_row()
    {
        await using var conn = new NpgsqlConnection(_pg.ConnectionString);
        await conn.OpenAsync();

        var docs = Enumerable.Range(1, 12)
            .Select(i => ((long)i, $"document {i} about topic number {i}"))
            .ToArray();
        await ResetAndSeed(conn, docs);

        await using var sp = BuildServices();
        var engine = sp.GetRequiredService<IFerretEngine>();
        var dialect = sp.GetRequiredService<ISqlDialect>();
        await using var session = CreateSession(_pg.ConnectionString, dialect);

        await engine.ReindexAsync<OvDoc>(session, "content", new ReindexOptions { BatchSize = 6 }, CancellationToken.None);

        // All 12 rows must have embeddings
        long totalRows, nullRows;
        await using (var nullCheck = conn.CreateCommand())
        {
            nullCheck.CommandText = """
                SELECT count(*), count(*) FILTER (WHERE content_embedding_v1 IS NULL)
                FROM ovdocs_vec;
                """;
            await using var nullReader = await nullCheck.ExecuteReaderAsync();
            await nullReader.ReadAsync();
            totalRows = nullReader.GetInt64(0);
            nullRows  = nullReader.GetInt64(1);
        }
        totalRows.Should().Be(12, "all 12 docs must be in the sidecar");
        nullRows.Should().Be(0, "no NULL embeddings after reindex");

        // Registry row must be stamped correctly
        string regModel, regCol, regStatus;
        int regDims;
        await using (var regCheck = conn.CreateCommand())
        {
            regCheck.CommandText = """
                SELECT model, dimensions, column_name, status
                FROM ferret_vector_versions
                WHERE entity = 'ovdocs' AND group_name = 'content' AND status = 'active';
                """;
            await using var regReader = await regCheck.ExecuteReaderAsync();
            (await regReader.ReadAsync()).Should().BeTrue("active registry row must exist");
            regModel  = regReader.GetString(0);
            regDims   = regReader.GetInt32(1);
            regCol    = regReader.GetString(2);
            regStatus = regReader.GetString(3);
        }
        regModel.Should().Be("nomic-embed-text");
        regDims.Should().Be(768);
        regCol.Should().Be("content_embedding_v1");
        regStatus.Should().Be("active");
    }

    [Fact]
    public async Task Search_returns_semantically_nearest_document()
    {
        await using var conn = new NpgsqlConnection(_pg.ConnectionString);
        await conn.OpenAsync();

        var docs = new (long Id, string Body)[]
        {
            (1L, "domestic cats and kittens as household pets"),
            (2L, "postgresql database indexing and query planning"),
            (3L, "italian pasta carbonara recipe"),
        };
        await ResetAndSeed(conn, docs);

        await using var sp = BuildServices();
        var engine = sp.GetRequiredService<IFerretEngine>();
        var dialect = sp.GetRequiredService<ISqlDialect>();
        await using var session = CreateSession(_pg.ConnectionString, dialect);

        await engine.ReindexAsync<OvDoc>(session, "content", new ReindexOptions { BatchSize = 10 }, CancellationToken.None);

        var result = await engine.SearchOffsetAsync<OvDoc, long>(session,
            new PagedQuery<OvDoc, long> { Mode = PaginationMode.Offset, Search = "small pet feline", Limit = 10 });

        result.Items.Should().NotBeEmpty();
        result.Items[0].Id.Should().Be(1L, "the cat doc must rank first for a feline-related query");
        result.Items[0].Body.Should().Be("domestic cats and kittens as household pets");
    }

    [Fact]
    public async Task Search_fails_loud_when_configured_model_differs_from_active_row()
    {
        await using var conn = new NpgsqlConnection(_pg.ConnectionString);
        await conn.OpenAsync();

        var docs = new (long Id, string Body)[]
        {
            (1L, "the quick brown fox"),
            (2L, "lazy dogs sleep all day"),
        };
        await ResetAndSeed(conn, docs);

        // First: reindex with the real Ollama provider — active row model = 'nomic-embed-text'
        await using var spOllama = BuildServices();
        var engineOllama = spOllama.GetRequiredService<IFerretEngine>();
        var dialectOllama = spOllama.GetRequiredService<ISqlDialect>();
        await using var sessionOllama = CreateSession(_pg.ConnectionString, dialectOllama);
        await engineOllama.ReindexAsync<OvDoc>(sessionOllama, "content", new ReindexOptions { BatchSize = 10 }, CancellationToken.None);

        // Second engine: same DB, but configured with FakeEmbeddingProvider (ModelId='fake', dims=768)
        // EmbedAsync succeeds (768d), but EnsureMatch will throw because stored model is 'nomic-embed-text'
        await using var spFake = BuildServicesWithProvider(
            v => v.UseEmbeddingProvider(_ => new FakeEmbeddingProvider(768)));
        var engineFake = spFake.GetRequiredService<IFerretEngine>();
        var dialectFake = spFake.GetRequiredService<ISqlDialect>();
        await using var sessionFake = CreateSession(_pg.ConnectionString, dialectFake);

        var act = async () => await engineFake.SearchOffsetAsync<OvDoc, long>(sessionFake,
            new PagedQuery<OvDoc, long> { Mode = PaginationMode.Offset, Search = "fox", Limit = 5 });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*embedding model changed*reindex required*");
    }
}
