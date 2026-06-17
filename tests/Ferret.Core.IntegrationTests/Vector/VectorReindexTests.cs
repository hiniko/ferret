using System.Data.Common;
using Ferret.Abstractions;
using Ferret.Core.Backends.Vector;
using Ferret.Core.Embeddings;
using Ferret.Core.Engine;
using Ferret.Core.IntegrationTests.Fixtures;
using Ferret.Hydration.Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Ferret.Core.IntegrationTests.Vector;

[Collection("pgvector")]
public class VectorReindexTests
{
    private readonly PgVectorFixture _fx;

    public VectorReindexTests(PgVectorFixture fx) => _fx = fx;

    [SearchableEntity(Table = "rvdocs")]
    public sealed class RVDoc : ISearchableEntity<long>
    {
        public long Id { get; init; }

        [Searchable(Backend = SearchBackend.Vector, Group = "content", EmbeddingDimensions = 8)]
        public string Body { get; init; } = "";
    }

    [SkippableFact]
    public async Task ReindexAsync_backfills_vector_sidecar()
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await ResetAndSeed(conn, rows: 50);

        var provider = new FakeEmbeddingProvider(8);
        var sc = new ServiceCollection();
        sc.AddLogging();
        sc.AddFerret(o => o
            .ScanAssembly(typeof(RVDoc).Assembly)
            .UsePostgres()
            .UseVectorSearch(v => v.UseEmbeddingProvider(_ => provider))
            .UseDapperHydration());
        await using var sp = sc.BuildServiceProvider();

        var engine = sp.GetRequiredService<IFerretEngine>();
        var dialect = sp.GetRequiredService<ISqlDialect>();

        var csb = new NpgsqlConnectionStringBuilder(_fx.ConnectionString) { PersistSecurityInfo = true };
        await using var session = new DapperSession(
            ct => Task.FromResult<DbConnection>(new NpgsqlConnection(csb.ConnectionString)),
            dialect);

        await engine.ReindexAsync<RVDoc>(session, "content", new ReindexOptions { BatchSize = 20 }, CancellationToken.None);

        await using var check = conn.CreateCommand();
        check.CommandText = "SELECT count(*), count(*) FILTER (WHERE content_embedding_v1 IS NULL) FROM rvdocs_vec;";
        await using var reader = await check.ExecuteReaderAsync();
        await reader.ReadAsync();
        reader.GetInt64(0).Should().Be(50);
        reader.GetInt64(1).Should().Be(0);
    }

    private static async Task ResetAndSeed(NpgsqlConnection conn, int rows)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            DROP TABLE IF EXISTS rvdocs_vec CASCADE;
            DROP TABLE IF EXISTS rvdocs CASCADE;
            CREATE TABLE rvdocs (
                id bigint PRIMARY KEY,
                body text NOT NULL
            );
            CREATE TABLE rvdocs_vec (
                id bigint PRIMARY KEY REFERENCES rvdocs(id) ON DELETE CASCADE,
                content_embedding_v1 vector(8),
                updated_at timestamptz NOT NULL DEFAULT now()
            );
            CREATE INDEX IF NOT EXISTS "ix_rvdocs_vec_content_embedding_v1_hnsw"
                ON rvdocs_vec USING hnsw (content_embedding_v1 vector_cosine_ops)
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

        if (rows == 0) return;
        await using var seed = conn.CreateCommand();
        seed.CommandText = $"""
            INSERT INTO rvdocs (id, body)
            SELECT g, 'body ' || g
            FROM generate_series(1, {rows}) AS g;
            """;
        await seed.ExecuteNonQueryAsync();
    }
}
