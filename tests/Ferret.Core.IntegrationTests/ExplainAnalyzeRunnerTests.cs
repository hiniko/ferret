#pragma warning disable CS0618 // PostgreSqlBuilder() parameterless ctor deprecated in 4.12; callers already pass .WithImage()
using Dapper;
using Ferret.Benchmarks.Infrastructure;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Ferret.Core.IntegrationTests;

public class ExplainAnalyzeRunnerTests
{
    [SkippableFact]
    public async Task Returns_plan_metrics_and_detects_seq_vs_index()
    {
        BenchGate.SkipUnlessEnabled();
        await using var harness = new ExplainSchemaHarness();
        try
        {
            await harness.StartAsync();
        }
        catch (Exception ex)
        {
            Skip.If(true, $"Docker unavailable: {ex.Message}");
            return;
        }

        const int depth = 2;
        await harness.SeedAsync();

        var fragment = GeneratedSqlCapture.Capture(depth, "needle");

        var result = await ExplainAnalyzeRunner.RunAsync(harness.ConnectionString, fragment);

        result.TotalCost.Should().NotBeNull();
        result.ActualTotalTimeMs.Should().NotBeNull();
        result.Scans.Should().NotBeEmpty();
        result.Scans.Should().OnlyContain(s => !string.IsNullOrWhiteSpace(s.NodeType));
        result.Scans.Should().Contain(s => s.IsSeqScan || s.IsIndexScan);
    }

    private sealed class ExplainSchemaHarness : IAsyncDisposable
    {
        private readonly Testcontainers.PostgreSql.PostgreSqlContainer _container =
            new Testcontainers.PostgreSql.PostgreSqlBuilder()
                .WithImage(Environment.GetEnvironmentVariable("FERRET_POSTGRES_IMAGE") ?? "postgres:17-alpine")
                .WithCleanUp(true)
                .Build();

        public string ConnectionString => _container.GetConnectionString();

        public async Task StartAsync()
        {
            await _container.StartAsync();
            await using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();
            await conn.ExecuteAsync("""
                CREATE EXTENSION IF NOT EXISTS pg_trgm;

                CREATE TABLE owner2 (
                    id   uuid PRIMARY KEY,
                    name text NOT NULL
                );
                CREATE TABLE h1_with_childs (
                    id       uuid PRIMARY KEY,
                    owner_id uuid NOT NULL REFERENCES owner2 (id),
                    label    text NOT NULL
                );
                CREATE TABLE h1s (
                    id        uuid PRIMARY KEY,
                    parent_id uuid NOT NULL REFERENCES h1_with_childs (id),
                    label     text NOT NULL
                );
                CREATE INDEX h1s_label_gist_trgm ON h1s USING gist (label gist_trgm_ops);
            """);
        }

        public async Task SeedAsync()
        {
            await using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();

            var ownerId = Guid.NewGuid();
            var hop1Id = Guid.NewGuid();
            await conn.ExecuteAsync(
                "INSERT INTO owner2 (id, name) VALUES (@Id, @Name)",
                new { Id = ownerId, Name = "owner" });
            await conn.ExecuteAsync(
                "INSERT INTO h1_with_childs (id, owner_id, label) VALUES (@Id, @OwnerId, @Label)",
                new { Id = hop1Id, OwnerId = ownerId, Label = "branch" });
            await conn.ExecuteAsync(
                "INSERT INTO h1s (id, parent_id, label) VALUES (@Id, @ParentId, @Label)",
                new[]
                {
                    new { Id = Guid.NewGuid(), ParentId = hop1Id, Label = "needle leaf" },
                    new { Id = Guid.NewGuid(), ParentId = hop1Id, Label = "haystack leaf" },
                });

            await conn.ExecuteAsync("ANALYZE owner2; ANALYZE h1_with_childs; ANALYZE h1s;");
        }

        public async ValueTask DisposeAsync() => await _container.DisposeAsync();
    }
}
