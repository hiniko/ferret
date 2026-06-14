#pragma warning disable CS0618 // PostgreSqlBuilder() parameterless ctor deprecated in 4.12; callers already pass .WithImage()
using Npgsql;
using Testcontainers.PostgreSql;

namespace Ferret.Benchmarks.Infrastructure;

public sealed class BenchPostgresHarness : IAsyncDisposable
{
    public static IReadOnlyList<string> ChainTables { get; } =
    [
        "bench_owner",
        "bench_hop1",
        "bench_hop2",
        "bench_hop3",
        "bench_hop4",
        "bench_hop5",
    ];

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage(Environment.GetEnvironmentVariable("FERRET_POSTGRES_IMAGE") ?? "postgres:17-alpine")
        .WithCleanUp(true)
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task StartAsync()
    {
        await _container.StartAsync();
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE EXTENSION IF NOT EXISTS pg_trgm;

            CREATE TABLE bench_owner (
                id   uuid PRIMARY KEY,
                name text NOT NULL
            );
            CREATE INDEX bench_owner_name_gist_trgm
                ON bench_owner USING gist (name gist_trgm_ops);

            CREATE TABLE bench_hop1 (
                id       uuid PRIMARY KEY,
                owner_id uuid NOT NULL REFERENCES bench_owner (id),
                label    text NOT NULL
            );
            CREATE INDEX bench_hop1_label_gist_trgm
                ON bench_hop1 USING gist (label gist_trgm_ops);

            CREATE TABLE bench_hop2 (
                id        uuid PRIMARY KEY,
                parent_id uuid NOT NULL REFERENCES bench_hop1 (id),
                label     text NOT NULL
            );
            CREATE INDEX bench_hop2_label_gist_trgm
                ON bench_hop2 USING gist (label gist_trgm_ops);

            CREATE TABLE bench_hop3 (
                id        uuid PRIMARY KEY,
                parent_id uuid NOT NULL REFERENCES bench_hop2 (id),
                label     text NOT NULL
            );
            CREATE INDEX bench_hop3_label_gist_trgm
                ON bench_hop3 USING gist (label gist_trgm_ops);

            CREATE TABLE bench_hop4 (
                id        uuid PRIMARY KEY,
                parent_id uuid NOT NULL REFERENCES bench_hop3 (id),
                label     text NOT NULL
            );
            CREATE INDEX bench_hop4_label_gist_trgm
                ON bench_hop4 USING gist (label gist_trgm_ops);

            CREATE TABLE bench_hop5 (
                id        uuid PRIMARY KEY,
                parent_id uuid NOT NULL REFERENCES bench_hop4 (id),
                label     text NOT NULL
            );
            CREATE INDEX bench_hop5_label_gist_trgm
                ON bench_hop5 USING gist (label gist_trgm_ops);
        """;
        await cmd.ExecuteNonQueryAsync();
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}
