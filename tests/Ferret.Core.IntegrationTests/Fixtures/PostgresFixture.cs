#pragma warning disable CS0618 // PostgreSqlBuilder() parameterless ctor deprecated in 4.12; callers already pass .WithImage()
using Dapper;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Ferret.Core.IntegrationTests.Fixtures;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage(Environment.GetEnvironmentVariable("FERRET_POSTGRES_IMAGE") ?? "postgres:17-alpine")
        .WithCleanUp(true)
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await _container.StartAsync();
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE EXTENSION IF NOT EXISTS pg_trgm;
            CREATE TABLE widgets (
                id uuid PRIMARY KEY,
                name text NOT NULL,
                sku  text NOT NULL,
                created_at timestamptz NOT NULL DEFAULT now()
            );
            CREATE INDEX widgets_name_gist_trgm ON widgets USING gist (name gist_trgm_ops);
            CREATE INDEX widgets_sku_gist_trgm  ON widgets USING gist (sku  gist_trgm_ops);
        """;
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();
}

[CollectionDefinition("postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture> { }
