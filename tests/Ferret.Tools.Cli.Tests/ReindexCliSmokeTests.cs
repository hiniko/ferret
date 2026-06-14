#pragma warning disable CS0618 // PostgreSqlBuilder() parameterless ctor deprecated in 4.12; callers already pass .WithImage()
using System.Reflection;
using Ferret.Abstractions;
using Ferret.Abstractions.Attributes;
using Ferret.Tools.Cli;
using FluentAssertions;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Ferret.Tools.Cli.Tests;

[SearchableEntity(Table = "cli_docs")]
[SearchGroup("content", FullTextConfig = "english")]
public sealed class CliDoc : ISearchableEntity<long>
{
    public long Id { get; init; }

    [Searchable(Backend = SearchBackend.FullText, Group = "content", Weight = 2.0f)]
    public string Title { get; init; } = "";

    [Searchable(Backend = SearchBackend.FullText, Group = "content", Weight = 1.0f)]
    public string Body { get; init; } = "";
}

public sealed class ReindexCliSmokeTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage(Environment.GetEnvironmentVariable("FERRET_POSTGRES_IMAGE") ?? "postgres:17-alpine")
        .WithCleanUp(true)
        .Build();

    private string ConnectionString => _container.GetConnectionString();

    private static readonly Assembly[] EntityAssemblies = [typeof(CliDoc).Assembly];

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            DROP TABLE IF EXISTS cli_docs_search CASCADE;
            DROP TABLE IF EXISTS cli_docs CASCADE;
            CREATE TABLE cli_docs (
                id bigint PRIMARY KEY,
                title text NOT NULL,
                body  text NOT NULL
            );
            CREATE TABLE cli_docs_search (
                id bigint PRIMARY KEY REFERENCES cli_docs(id) ON DELETE CASCADE,
                content_tsv tsvector,
                updated_at timestamptz NOT NULL DEFAULT now()
            );
            INSERT INTO cli_docs (id, title, body)
            SELECT g, 'title ' || g, 'body ' || g FROM generate_series(1, 120) AS g;
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public async Task Reindex_enqueues_and_drains_exit_zero()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exit = await FerretCli.InvokeAsync(
            ["reindex", "--entity", "CliDoc", "--group", "content", "--batch-size", "50", "--connection", ConnectionString],
            EntityAssemblies, output, error);

        exit.Should().Be(0, error.ToString());

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        await using (var job = conn.CreateCommand())
        {
            job.CommandText = """
                SELECT "status", "last_id"
                FROM "ferret_reindex_jobs"
                ORDER BY "id" DESC LIMIT 1;
                """;
            await using var jr = await job.ExecuteReaderAsync();
            (await jr.ReadAsync()).Should().BeTrue();
            jr.GetString(0).Should().Be("done");
            jr.GetString(1).Should().Be("120");
        }

        await using var check = conn.CreateCommand();
        check.CommandText = """
            SELECT count(*), count(*) FILTER (WHERE content_tsv IS NULL)
            FROM cli_docs_search;
            """;
        await using var reader = await check.ExecuteReaderAsync();
        await reader.ReadAsync();
        reader.GetInt64(0).Should().Be(120);
        reader.GetInt64(1).Should().Be(0);
    }

    [Fact]
    public async Task Reindex_resolves_entity_from_assembly_path_and_drains()
    {
        // Exercise resolution OUTSIDE the in-process seam: load the assembly by
        // path the way the real `dotnet ferret --assembly <path>` flow does.
        var assemblyPath = typeof(CliDoc).Assembly.Location;

        var resolved = EntityAssemblyResolver.Resolve(
            assemblyPaths: [assemblyPath],
            workingDirectory: Path.GetDirectoryName(assemblyPath)!);

        resolved.Should().ContainSingle();
        resolved[0].GetName().Name.Should().Be(typeof(CliDoc).Assembly.GetName().Name);

        var output = new StringWriter();
        var error = new StringWriter();

        var exit = await FerretCli.InvokeAsync(
            ["reindex", "--entity", "CliDoc", "--group", "content", "--batch-size", "50", "--connection", ConnectionString],
            resolved, output, error);

        exit.Should().Be(0, error.ToString());

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var check = conn.CreateCommand();
        check.CommandText = """
            SELECT count(*), count(*) FILTER (WHERE content_tsv IS NULL)
            FROM cli_docs_search;
            """;
        await using var reader = await check.ExecuteReaderAsync();
        await reader.ReadAsync();
        reader.GetInt64(0).Should().Be(120);
        reader.GetInt64(1).Should().Be(0);
    }

    [Fact]
    public void Resolve_auto_discovers_searchable_assembly_in_working_directory()
    {
        var directory = Path.GetDirectoryName(typeof(CliDoc).Assembly.Location)!;

        var resolved = EntityAssemblyResolver.Resolve(
            assemblyPaths: [],
            workingDirectory: directory);

        resolved.Should().Contain(a => a.GetName().Name == typeof(CliDoc).Assembly.GetName().Name);
    }

    [Fact]
    public async Task ReindexStatus_unknown_entity_nonzero()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exit = await FerretCli.InvokeAsync(
            ["reindex-status", "--entity", "NoSuchEntity", "--connection", ConnectionString],
            EntityAssemblies, output, error);

        exit.Should().NotBe(0);
        error.ToString().Should().Contain("NoSuchEntity");
    }
}
