using Dapper;
using Ferret.Benchmarks.Infrastructure;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Ferret.Core.IntegrationTests;

public class BenchPostgresHarnessTests
{
    [SkippableFact]
    public async Task Harness_creates_chain_tables_and_trgm_indexes()
    {
        BenchGate.SkipUnlessEnabled();
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

        await using var conn = new NpgsqlConnection(harness.ConnectionString);
        await conn.OpenAsync();

        foreach (var table in BenchPostgresHarness.ChainTables)
        {
            var exists = await conn.ExecuteScalarAsync<bool>(
                "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'public' AND table_name = @table)",
                new { table });
            exists.Should().BeTrue($"table {table} should exist");

            var trgmIndexes = await conn.ExecuteScalarAsync<long>(
                "SELECT count(*) FROM pg_indexes WHERE schemaname = 'public' AND tablename = @table AND indexdef ILIKE '%gist_trgm_ops%'",
                new { table });
            trgmIndexes.Should().BeGreaterThan(0, $"table {table} should have a gist_trgm_ops index");
        }

        var trgmEnabled = await conn.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'pg_trgm')");
        trgmEnabled.Should().BeTrue();
    }
}
