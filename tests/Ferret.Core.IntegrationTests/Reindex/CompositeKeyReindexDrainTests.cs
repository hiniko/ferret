using Ferret.Abstractions.Attributes;
using Ferret.Abstractions.Search;
using Ferret.Core.Backends.FullText;
using Ferret.Core.Engine.Reindex;
using Ferret.Core.IntegrationTests.Fixtures;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Ferret.Core.IntegrationTests.Reindex;

[Collection("postgres")]
public class CompositeKeyReindexDrainTests
{
    private const string SourceTable  = "ck_reindex_docs";
    private const string SidecarTable = "ck_reindex_docs_search";
    private const string ColumnSuffix = "_tsv";

    private static readonly string[] KeyColumns = ["tenant_id", "id"];

    private readonly PostgresFixture _fx;

    public CompositeKeyReindexDrainTests(PostgresFixture fx) => _fx = fx;

    private static FullTextGroup[] Groups() =>
    [
        new FullTextGroup
        {
            Name = "content",
            FullTextConfig = "english",
            Reindex = ReindexMode.Concurrent,
            Properties =
            [
                new FullTextGroupProperty { PropertyName = "Title", ColumnName = "title", Weight = FullTextWeightBucket.A },
                new FullTextGroupProperty { PropertyName = "Body",  ColumnName = "body",  Weight = FullTextWeightBucket.B },
            ],
        },
    ];

    private static ReindexRangeRequest Request() => new()
    {
        SidecarTable  = SidecarTable,
        SidecarSchema = null,
        SourceTable   = SourceTable,
        SourceSchema  = null,
        IdColumn      = KeyColumns[0],
        KeyColumns    = KeyColumns,
        ColumnSuffix  = ColumnSuffix,
        Groups        = Groups(),
        BatchSize     = 40,
        BatchDelay    = TimeSpan.Zero,
    };

    [Fact]
    public async Task DrainAsync_composite_key_populates_every_sidecar_row_across_batches()
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();

        var tenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var tenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        await Exec(conn, $"""
            DROP TABLE IF EXISTS {SidecarTable} CASCADE;
            DROP TABLE IF EXISTS {SourceTable} CASCADE;
            CREATE TABLE {SourceTable} (
                tenant_id uuid NOT NULL,
                id bigint NOT NULL,
                title text NOT NULL,
                body  text NOT NULL,
                PRIMARY KEY (tenant_id, id)
            );
            CREATE TABLE {SidecarTable} (
                tenant_id uuid NOT NULL,
                id bigint NOT NULL,
                content_tsv tsvector,
                updated_at timestamptz NOT NULL DEFAULT now(),
                PRIMARY KEY (tenant_id, id),
                FOREIGN KEY (tenant_id, id) REFERENCES {SourceTable}(tenant_id, id) ON DELETE CASCADE
            );
            """);

        // 100 rows per tenant: forces multiple batches (batch size 40) and exercises
        // the row-value keyset advance across the composite key boundary.
        await Exec(conn, $"""
            INSERT INTO {SourceTable} (tenant_id, id, title, body)
            SELECT '{tenantA}', g, 'title ' || g, 'body ' || g FROM generate_series(1, 100) AS g;
            INSERT INTO {SourceTable} (tenant_id, id, title, body)
            SELECT '{tenantB}', g, 'title ' || g, 'body ' || g FROM generate_series(1, 100) AS g;
            """);

        await Exec(conn,
            FullTextDdlBuilder.EnsureReindexJobsTable() +
            "TRUNCATE TABLE \"ferret_reindex_jobs\" RESTART IDENTITY;" +
            "INSERT INTO \"ferret_reindex_jobs\" (\"entity\", \"group_name\", \"status\", \"batch_size\") " +
            "VALUES ('ck_reindex_docs', 'content', 'pending', 40);");

        var processor = new ReindexJobProcessor();
        await using var drainConn = new NpgsqlConnection(_fx.ConnectionString);
        await drainConn.OpenAsync();
        var processed = await processor.DrainAsync(
            drainConn,
            TimeSpan.FromMinutes(5),
            _ => Request(),
            CancellationToken.None);

        processed.Should().Be(1);

        await using (var job = conn.CreateCommand())
        {
            job.CommandText = """
                SELECT "status", "error"
                FROM "ferret_reindex_jobs"
                ORDER BY "id" DESC LIMIT 1;
                """;
            await using var jr = await job.ExecuteReaderAsync();
            await jr.ReadAsync();
            jr.GetString(0).Should().Be("done");
            jr.IsDBNull(1).Should().BeTrue();
        }

        // Every one of the 200 composite-key rows is in the sidecar (ON CONFLICT
        // upsert worked) and every tsv is populated (keyset advanced with no gaps).
        await using var check = conn.CreateCommand();
        check.CommandText = $"""
            SELECT count(*), count(*) FILTER (WHERE content_tsv IS NULL)
            FROM {SidecarTable};
            """;
        await using var reader = await check.ExecuteReaderAsync();
        await reader.ReadAsync();
        reader.GetInt64(0).Should().Be(200);
        reader.GetInt64(1).Should().Be(0);
    }

    private static async Task Exec(NpgsqlConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}
