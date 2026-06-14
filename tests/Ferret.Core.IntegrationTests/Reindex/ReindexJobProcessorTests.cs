using System.Diagnostics;
using Ferret.Core.Engine.Reindex;
using Ferret.Core.IntegrationTests.Fixtures;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Ferret.Core.IntegrationTests.Reindex;

[Collection("postgres")]
public class ReindexJobProcessorTests
{
    private readonly PostgresFixture _fx;

    public ReindexJobProcessorTests(PostgresFixture fx) => _fx = fx;

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
    public async Task RunRange_populates_sidecar_in_batches()
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await ReindexTestSchema.ResetAsync(conn);
        await ReindexTestSchema.SeedAsync(conn, 250);

        var committed = new List<object>();
        var batchActivities = 0;
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "Ferret.Core",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = a =>
            {
                if (a.OperationName == "ferret.reindex.batch")
                    Interlocked.Increment(ref batchActivities);
            },
        };
        ActivitySource.AddActivityListener(listener);

        var processor = new ReindexJobProcessor();
        var lastId = await processor.RunRangeAsync(
            conn, Request(), id => { committed.Add(id); return Task.CompletedTask; }, CancellationToken.None);

        // 250 rows, batch 100 => 3 batches
        committed.Should().HaveCount(3);
        committed.Select(Convert.ToInt64).Should().Equal(100L, 200L, 250L);
        batchActivities.Should().Be(3);

        // last_id = max key
        Convert.ToInt64(lastId).Should().Be(250L);

        // every row indexed, no gaps / dupes
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
    public async Task RunRange_empty_table_runs_zero_batches()
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await ReindexTestSchema.ResetAsync(conn);

        var committed = new List<object>();
        var processor = new ReindexJobProcessor();
        var lastId = await processor.RunRangeAsync(
            conn, Request(), id => { committed.Add(id); return Task.CompletedTask; }, CancellationToken.None);

        committed.Should().BeEmpty();
        lastId.Should().BeNull();

        await using var check = conn.CreateCommand();
        check.CommandText = $"SELECT count(*) FROM {ReindexTestSchema.SidecarTable};";
        var n = (long)(await check.ExecuteScalarAsync())!;
        n.Should().Be(0);
    }
}
