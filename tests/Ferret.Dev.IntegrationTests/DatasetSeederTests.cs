using Dapper;
using Ferret.Benchmarks.Infrastructure;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Ferret.Core.IntegrationTests;

public class DatasetSeederTests
{
    [SkippableFact]
    public async Task Seed_produces_expected_row_counts_and_known_match()
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

        const int depth = 3;
        const int ownerCount = 100;
        const int fanOut = 2;

        await using var conn = new NpgsqlConnection(harness.ConnectionString);
        await conn.OpenAsync();

        var result = await DatasetSeeder.SeedAsync(
            harness.ConnectionString,
            new DatasetSeedSpec
            {
                Depth = depth,
                OwnerCount = ownerCount,
                FanOut = fanOut,
                Seed = 12345,
            });

        (await conn.ExecuteScalarAsync<long>("SELECT count(*) FROM bench_owner"))
            .Should().Be(ownerCount);
        (await conn.ExecuteScalarAsync<long>("SELECT count(*) FROM bench_hop1"))
            .Should().Be(ownerCount * (long)fanOut);
        (await conn.ExecuteScalarAsync<long>("SELECT count(*) FROM bench_hop2"))
            .Should().Be(ownerCount * (long)fanOut * fanOut);
        (await conn.ExecuteScalarAsync<long>("SELECT count(*) FROM bench_hop3"))
            .Should().Be(ownerCount * (long)fanOut * fanOut * fanOut);

        (await conn.ExecuteScalarAsync<long>("SELECT count(*) FROM bench_hop4"))
            .Should().Be(0);
        (await conn.ExecuteScalarAsync<long>("SELECT count(*) FROM bench_hop5"))
            .Should().Be(0);

        result.MatchToken.Should().NotBeNullOrWhiteSpace();
        result.ExpectedOwnerIds.Should().NotBeEmpty();

        var leafMatches = await conn.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM bench_hop3 WHERE label LIKE @pattern",
            new { pattern = $"%{result.MatchToken}%" });
        leafMatches.Should().BeGreaterThan(0);

        var ownersWithMatch = (await conn.QueryAsync<Guid>(
            """
            SELECT DISTINCT o.id
            FROM bench_owner o
            JOIN bench_hop1 h1 ON h1.owner_id  = o.id
            JOIN bench_hop2 h2 ON h2.parent_id = h1.id
            JOIN bench_hop3 h3 ON h3.parent_id = h2.id
            WHERE h3.label LIKE @pattern
            """,
            new { pattern = $"%{result.MatchToken}%" })).ToHashSet();

        ownersWithMatch.Should().BeEquivalentTo(result.ExpectedOwnerIds);
    }

    [SkippableFact]
    public async Task Seed_is_deterministic_for_fixed_seed()
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

        var spec = new DatasetSeedSpec { Depth = 2, OwnerCount = 10, FanOut = 2, Seed = 99 };

        var first = await DatasetSeeder.SeedAsync(harness.ConnectionString, spec);
        var second = await DatasetSeeder.SeedAsync(harness.ConnectionString, spec);

        second.MatchToken.Should().Be(first.MatchToken);
        second.ExpectedOwnerIds.Should().BeEquivalentTo(first.ExpectedOwnerIds);
    }
}
