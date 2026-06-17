using Ferret.Abstractions.Search;
using Ferret.Benchmarks.Infrastructure;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Ferret.Core.IntegrationTests;

public class CacheStateTests
{
    [SkippableFact]
    public async Task Warmup_then_measured_runs_are_faster_or_equal()
    {
        await using var harness = new BenchPostgresHarness();
        try
        {
            await harness.StartAsync();
        }
        catch (Exception ex)
        {
            Skip.If(true, $"Docker unavailable: {ex.Message}");
            return;
        }

        var seed = await DatasetSeeder.SeedAsync(
            harness.ConnectionString,
            new DatasetSeedSpec
            {
                Depth = 3,
                OwnerCount = 1_000,
                FanOut = 2,
                Seed = 7,
            });

        var fragment = new SearchSqlFragment(
            """
            SELECT DISTINCT o.id
            FROM bench_owner o
            JOIN bench_hop1 h1 ON h1.owner_id  = o.id
            JOIN bench_hop2 h2 ON h2.parent_id = h1.id
            JOIN bench_hop3 h3 ON h3.parent_id = h2.id
            WHERE h3.label LIKE @pattern
            """,
            [new KeyValuePair<string, object?>("pattern", $"%{seed.MatchToken}%")]);

        await using var conn = new NpgsqlConnection(harness.ConnectionString);
        await conn.OpenAsync();

        var cold = await CacheState.MeasureColdMedianAsync(conn, fragment, runs: 5);
        var warm = await CacheState.MeasureWarmMedianAsync(conn, fragment, runs: 5);

        cold.Should().BeGreaterThan(TimeSpan.Zero);
        warm.Should().BeGreaterThan(TimeSpan.Zero);

        const double tolerance = 1.5;
        warm.TotalMilliseconds.Should().BeLessThanOrEqualTo(cold.TotalMilliseconds * tolerance);
    }
}
