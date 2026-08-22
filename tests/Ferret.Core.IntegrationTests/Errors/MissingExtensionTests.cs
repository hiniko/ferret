#pragma warning disable CS0618 // PostgreSqlBuilder() parameterless ctor deprecated in 4.12; callers already pass .WithImage()
using System.Data.Common;
using Dapper;
using Ferret.Abstractions;
using Ferret.Hydration.Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Ferret.Core.IntegrationTests.Errors;

/// <summary>
/// Own isolated container/collection (not the shared "postgres" fixture): this test
/// deliberately runs WITHOUT pg_trgm so it must not share state/schema with any other
/// test's connection.
/// </summary>
public sealed class MissingExtensionFixture : IAsyncLifetime
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
        // No CREATE EXTENSION pg_trgm here: the point of this fixture is a database
        // that hasn't had the Ferret migration/extension applied yet.
        cmd.CommandText = """
            CREATE TABLE widgets (
                id uuid PRIMARY KEY,
                name text NOT NULL
            );
        """;
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();
}

[CollectionDefinition("missing-extension")]
public sealed class MissingExtensionCollection : ICollectionFixture<MissingExtensionFixture> { }

[Collection("missing-extension")]
public class MissingExtensionTests
{
    private readonly MissingExtensionFixture _fx;

    public MissingExtensionTests(MissingExtensionFixture fx) => _fx = fx;

    [SearchableEntity(Table = "widgets")]
    public sealed class Widget : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }
        [Searchable, Filterable] public string Name { get; init; } = "";
    }

    [Fact]
    public async Task Search_without_pg_trgm_throws_actionable_error()
    {
        var sp = BuildServices();
        var engine = sp.GetRequiredService<IFerretEngine>();
        var dialect = sp.GetRequiredService<ISqlDialect>();

        await using var session = new DapperSession(
            ct => Task.FromResult<DbConnection>(new NpgsqlConnection(_fx.ConnectionString)),
            dialect);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            engine.SearchOffsetAsync<Widget, Guid>(session, new PagedQuery<Widget, Guid>
            {
                Mode = PaginationMode.Offset,
                Search = "widget",
                Limit = 10,
            }));

        ex.Message.Should().Contain("required PostgreSQL extension is missing");
        ex.Message.Should().Contain("pg_trgm for trigram search");
        ex.Message.Should().Contain("vector for vector search");
        ex.Message.Should().Contain("docs/migrations.md");
        ex.InnerException.Should().BeOfType<PostgresException>();
    }

    private ServiceProvider BuildServices()
    {
        var sc = new ServiceCollection();
        sc.AddLogging();
        sc.AddFerret(opts => opts
            .ScanAssembly(typeof(Widget).Assembly)
            .UseTrigramSearch());
        return sc.BuildServiceProvider();
    }
}
