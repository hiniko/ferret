#pragma warning disable CS0618 // PostgreSqlBuilder() parameterless ctor deprecated in 4.12; .WithImage() is passed
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Ferret.EntityFrameworkCore.Tests;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage(Environment.GetEnvironmentVariable("FERRET_POSTGRES_IMAGE") ?? "postgres:17-alpine")
        .WithCleanUp(true)
        .Build();

    /// <summary>Connection string to the container's default database.</summary>
    public string ConnectionString => _container.GetConnectionString();

    /// <summary>
    /// A connection string pointing at a fresh, uniquely-named database. EnsureCreated creates it on
    /// first use, isolating schema-building tests from each other on the shared container.
    /// </summary>
    public string UniqueConnectionString() =>
        new NpgsqlConnectionStringBuilder(ConnectionString)
        {
            Database = "t_" + Guid.NewGuid().ToString("N"),
        }.ConnectionString;

    public Task InitializeAsync() => _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync();
}

[CollectionDefinition("postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture> { }
