using Ferret.Core.Backends.FullText;
using Ferret.Core.Engine.Reindex;
using Ferret.Core.IntegrationTests.Fixtures;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Ferret.Core.IntegrationTests.Reindex;

[Collection("postgres")]
public class ReindexFailureRecoveryTests
{
    private readonly PostgresFixture _fx;

    public ReindexFailureRecoveryTests(PostgresFixture fx) => _fx = fx;

    private static ReindexRangeRequest Request() => new()
    {
        SidecarTable  = ReindexTestSchema.SidecarTable,
        SidecarSchema = null,
        SourceTable   = ReindexTestSchema.SourceTable,
        SourceSchema  = null,
        IdColumn      = ReindexTestSchema.IdColumn,
        ColumnSuffix  = ReindexTestSchema.ColumnSuffix,
        Groups        = ReindexTestSchema.Groups(),
        BatchSize     = 100,
        BatchDelay    = TimeSpan.Zero,
    };

    private static async Task SeedJobAsync(NpgsqlConnection conn)
    {
        await using var ddl = conn.CreateCommand();
        ddl.CommandText = FullTextDdlBuilder.EnsureReindexJobsTable() +
            "TRUNCATE TABLE \"ferret_reindex_jobs\" RESTART IDENTITY;" +
            "INSERT INTO \"ferret_reindex_jobs\" (\"entity\", \"group_name\", \"status\", \"batch_size\") " +
            "VALUES ('reindex_docs', 'content', 'pending', 100);";
        await ddl.ExecuteNonQueryAsync();
    }

    private static async Task RunSqlAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task DrainAsync_resumes_failed_job_from_last_id()
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await ReindexTestSchema.ResetAsync(conn);
        await ReindexTestSchema.SeedAsync(conn, 250);
        await SeedJobAsync(conn);

        // Fault: any sidecar row past the first batch (id > 100) raises, so the
        // first batch (1..100) commits but the second fails mid-backfill.
        await RunSqlAsync(conn, $"""
            CREATE FUNCTION reindex_fault() RETURNS trigger AS $$
            BEGIN
                IF NEW.id > 100 THEN
                    RAISE EXCEPTION 'injected fault';
                END IF;
                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;
            CREATE TRIGGER reindex_fault_trg
                BEFORE INSERT ON {ReindexTestSchema.SidecarTable}
                FOR EACH ROW EXECUTE FUNCTION reindex_fault();
            """);

        var processor = new ReindexJobProcessor();
        await using var drainConn = new NpgsqlConnection(_fx.ConnectionString);
        await drainConn.OpenAsync();

        var processed = await processor.DrainAsync(
            drainConn,
            TimeSpan.FromMinutes(5),
            _ => Request(),
            CancellationToken.None);

        processed.Should().Be(0);

        await using (var job = conn.CreateCommand())
        {
            job.CommandText = """
                SELECT "status", "last_id", "error"
                FROM "ferret_reindex_jobs"
                ORDER BY "id" DESC LIMIT 1;
                """;
            await using var jr = await job.ExecuteReaderAsync();
            await jr.ReadAsync();
            jr.GetString(0).Should().Be("failed");
            jr.GetString(1).Should().Be("100");
            jr.IsDBNull(2).Should().BeFalse();
            jr.GetString(2).Should().Contain("injected fault");
        }

        // Clear the fault and re-run; the failed job is re-claimed and resumes.
        await RunSqlAsync(conn, $"""
            DROP TRIGGER reindex_fault_trg ON {ReindexTestSchema.SidecarTable};
            DROP FUNCTION reindex_fault();
            """);

        var processedAgain = await processor.DrainAsync(
            drainConn,
            TimeSpan.FromMinutes(5),
            _ => Request(),
            CancellationToken.None);

        processedAgain.Should().Be(1);

        await using (var job = conn.CreateCommand())
        {
            job.CommandText = """
                SELECT "status", "last_id", "error"
                FROM "ferret_reindex_jobs"
                ORDER BY "id" DESC LIMIT 1;
                """;
            await using var jr = await job.ExecuteReaderAsync();
            await jr.ReadAsync();
            jr.GetString(0).Should().Be("done");
            jr.GetString(1).Should().Be("250");
            jr.IsDBNull(2).Should().BeTrue();
        }

        await using var check = conn.CreateCommand();
        check.CommandText = $"""
            SELECT count(*),
                   count(*) FILTER (WHERE content_tsv IS NULL),
                   (SELECT count(*) FROM (SELECT id FROM {ReindexTestSchema.SidecarTable} GROUP BY id HAVING count(*) > 1) d)
            FROM {ReindexTestSchema.SidecarTable};
            """;
        await using var reader = await check.ExecuteReaderAsync();
        await reader.ReadAsync();
        reader.GetInt64(0).Should().Be(250);
        reader.GetInt64(1).Should().Be(0);
        reader.GetInt64(2).Should().Be(0);
    }

    [Fact]
    public async Task DrainAsync_reclaims_stale_running_job()
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await ReindexTestSchema.ResetAsync(conn);
        await ReindexTestSchema.SeedAsync(conn, 250);

        await using (var ddl = conn.CreateCommand())
        {
            ddl.CommandText = FullTextDdlBuilder.EnsureReindexJobsTable() +
                "TRUNCATE TABLE \"ferret_reindex_jobs\" RESTART IDENTITY;" +
                "INSERT INTO \"ferret_reindex_jobs\" " +
                "(\"entity\", \"group_name\", \"status\", \"batch_size\", \"last_id\", \"started_at\") " +
                "VALUES ('reindex_docs', 'content', 'running', 100, '100', now() - interval '1 hour');";
            await ddl.ExecuteNonQueryAsync();
        }

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
                SELECT "status", "last_id"
                FROM "ferret_reindex_jobs"
                ORDER BY "id" DESC LIMIT 1;
                """;
            await using var jr = await job.ExecuteReaderAsync();
            await jr.ReadAsync();
            jr.GetString(0).Should().Be("done");
            jr.GetString(1).Should().Be("250");
        }

        await using var check = conn.CreateCommand();
        check.CommandText = $"""
            SELECT count(*), count(*) FILTER (WHERE content_tsv IS NULL)
            FROM {ReindexTestSchema.SidecarTable};
            """;
        await using var reader = await check.ExecuteReaderAsync();
        await reader.ReadAsync();
        // Resumed from last_id=100, so rows 101..250 indexed.
        reader.GetInt64(0).Should().Be(150);
        reader.GetInt64(1).Should().Be(0);
    }

    [Fact]
    public async Task DrainAsync_unknown_entity_marks_failed()
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await ReindexTestSchema.ResetAsync(conn);
        await ReindexTestSchema.SeedAsync(conn, 10);
        await SeedJobAsync(conn);

        var processor = new ReindexJobProcessor();
        await using var drainConn = new NpgsqlConnection(_fx.ConnectionString);
        await drainConn.OpenAsync();
        var processed = await processor.DrainAsync(
            drainConn,
            TimeSpan.FromMinutes(5),
            _ => throw new InvalidOperationException("unknown entity reindex_docs/content"),
            CancellationToken.None);

        processed.Should().Be(0);

        await using var job = conn.CreateCommand();
        job.CommandText = """
            SELECT "status", "error"
            FROM "ferret_reindex_jobs"
            ORDER BY "id" DESC LIMIT 1;
            """;
        await using var jr = await job.ExecuteReaderAsync();
        await jr.ReadAsync();
        jr.GetString(0).Should().Be("failed");
        jr.IsDBNull(1).Should().BeFalse();
        jr.GetString(1).Should().Contain("unknown entity");
    }
}
