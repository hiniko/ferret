using Ferret.Core.Backends.FullText;
using Ferret.Core.Engine.Reindex;
using Ferret.Core.IntegrationTests.Fixtures;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace Ferret.Core.IntegrationTests.Reindex;

[Collection("postgres")]
public class ReindexAdvisoryLockTests
{
    private readonly PostgresFixture _fx;

    public ReindexAdvisoryLockTests(PostgresFixture fx) => _fx = fx;

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

    private sealed class CountingLogger : ILogger
    {
        public int LockSkips;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Debug && formatter(state, exception).Contains("lock"))
                Interlocked.Increment(ref LockSkips);
        }
    }

    [Fact]
    public async Task DrainAsync_advisory_lock_prevents_double_processing()
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

        var firstClaimed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondJobs = 0;

        var loggerA = new CountingLogger();
        var loggerB = new CountingLogger();

        await using var connA = new NpgsqlConnection(_fx.ConnectionString);
        await connA.OpenAsync();
        await using var connB = new NpgsqlConnection(_fx.ConnectionString);
        await connB.OpenAsync();

        var first = processor.DrainAsync(
            connA,
            TimeSpan.FromMinutes(5),
            _ => Request(),
            CancellationToken.None,
            loggerA,
            async _ =>
            {
                firstClaimed.TrySetResult();
                await releaseFirst.Task;
            });

        await firstClaimed.Task;

        var second = processor.DrainAsync(
            connB,
            TimeSpan.FromMinutes(5),
            _ => Request(),
            CancellationToken.None,
            loggerB,
            _ => { Interlocked.Increment(ref secondJobs); return Task.CompletedTask; });

        var secondProcessed = await second;
        releaseFirst.TrySetResult();
        var firstProcessed = await first;

        secondProcessed.Should().Be(0);
        secondJobs.Should().Be(0);
        firstProcessed.Should().Be(1);

        loggerB.LockSkips.Should().BeGreaterThan(0);

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
            SELECT count(*),
                   count(*) FILTER (WHERE content_tsv IS NULL)
            FROM {ReindexTestSchema.SidecarTable};
            """;
        await using var reader = await check.ExecuteReaderAsync();
        await reader.ReadAsync();
        reader.GetInt64(0).Should().Be(250);
        reader.GetInt64(1).Should().Be(0);
    }
}
