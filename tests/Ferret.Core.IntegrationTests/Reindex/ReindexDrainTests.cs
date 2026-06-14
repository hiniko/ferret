using Ferret.Core.Backends.FullText;
using Ferret.Core.Engine.Reindex;
using Ferret.Core.IntegrationTests.Fixtures;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Ferret.Core.IntegrationTests.Reindex;

[Collection("postgres")]
public class ReindexDrainTests
{
    private readonly PostgresFixture _fx;

    public ReindexDrainTests(PostgresFixture fx) => _fx = fx;

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

    [Fact]
    public async Task DrainAsync_claim_marks_job_done()
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await ReindexTestSchema.ResetAsync(conn);
        await ReindexTestSchema.SeedAsync(conn, 250);

        await using (var ddl = conn.CreateCommand())
        {
            ddl.CommandText = FullTextDdlBuilder.EnsureReindexJobsTable() +
                "TRUNCATE TABLE \"ferret_reindex_jobs\" RESTART IDENTITY;" +
                "INSERT INTO \"ferret_reindex_jobs\" (\"entity\", \"group_name\", \"status\", \"batch_size\") " +
                "VALUES ('reindex_docs', 'content', 'pending', 100);";
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
                SELECT "status", "last_id", "started_at", "finished_at", "error"
                FROM "ferret_reindex_jobs"
                ORDER BY "id" DESC LIMIT 1;
                """;
            await using var jr = await job.ExecuteReaderAsync();
            await jr.ReadAsync();
            jr.GetString(0).Should().Be("done");
            jr.GetString(1).Should().Be("250");
            jr.IsDBNull(2).Should().BeFalse();
            jr.IsDBNull(3).Should().BeFalse();
            jr.IsDBNull(4).Should().BeTrue();
        }

        await using var check = conn.CreateCommand();
        check.CommandText = $"""
            SELECT count(*),
                   count(*) FILTER (WHERE content_tsv IS NULL)
            FROM {ReindexTestSchema.SidecarTable};
            """;
        await using var reader = await check.ExecuteReaderAsync();
        await reader.ReadAsync();
        reader.GetInt64(0).Should().Be(250);
        reader.GetInt64(1).Should().Be(0);
    }

    [Fact]
    public async Task DrainAsync_collapses_duplicate_pending_rows_into_one_unit()
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await ReindexTestSchema.ResetAsync(conn);
        await ReindexTestSchema.SeedAsync(conn, 250);

        // Five pending rows for the same (entity, group_name) — the owner-key
        // enqueue fan-out shape. They must collapse to a single claimed unit.
        await using (var ddl = conn.CreateCommand())
        {
            ddl.CommandText = FullTextDdlBuilder.EnsureReindexJobsTable() +
                "TRUNCATE TABLE \"ferret_reindex_jobs\" RESTART IDENTITY;" +
                "INSERT INTO \"ferret_reindex_jobs\" (\"entity\", \"group_name\", \"status\", \"batch_size\") " +
                "SELECT 'reindex_docs', 'content', 'pending', 100 FROM generate_series(1, 5);";
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

        // Exactly one job processed — not five.
        processed.Should().Be(1);

        await using (var jobs = conn.CreateCommand())
        {
            jobs.CommandText = """
                SELECT count(*),
                       count(*) FILTER (WHERE "status" = 'done')
                FROM "ferret_reindex_jobs"
                WHERE "entity" = 'reindex_docs' AND "group_name" = 'content';
                """;
            await using var jr = await jobs.ExecuteReaderAsync();
            await jr.ReadAsync();
            // Duplicates deleted: a single surviving row, marked done.
            jr.GetInt64(0).Should().Be(1);
            jr.GetInt64(1).Should().Be(1);
        }

        await using var check = conn.CreateCommand();
        check.CommandText = $"""
            SELECT count(*), count(*) FILTER (WHERE content_tsv IS NULL)
            FROM {ReindexTestSchema.SidecarTable};
            """;
        await using var reader = await check.ExecuteReaderAsync();
        await reader.ReadAsync();
        reader.GetInt64(0).Should().Be(250);
        reader.GetInt64(1).Should().Be(0);
    }
}
