#pragma warning disable CS0618 // PostgreSqlBuilder() parameterless ctor deprecated in 4.12; callers already pass .WithImage()
using Dapper;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Ferret.Core.IntegrationTests.Fixtures;

public sealed class PgVectorFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage(Environment.GetEnvironmentVariable("FERRET_PGVECTOR_IMAGE") ?? "pgvector/pgvector:pg17")
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
        cmd.CommandText = "CREATE EXTENSION IF NOT EXISTS vector;";
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();
}

[CollectionDefinition("pgvector")]
public sealed class PgVectorCollection : ICollectionFixture<PgVectorFixture> { }
