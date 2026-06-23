#pragma warning disable CS0618 // PostgreSqlBuilder() parameterless ctor deprecated in 4.12; callers already pass .WithImage()
using System.Data.Common;
using Ferret.Abstractions;
using Ferret.Abstractions.Attributes;
using Ferret.Abstractions.Search;
using Ferret.Abstractions.Session;
using Ferret.Abstractions.Sql;
using Ferret.Core.Backends.FullText;
using Ferret.Core.Configuration;
using Ferret.Core.DependencyInjection;
using Ferret.Hosting;
using Ferret.Hosting.DependencyInjection;
using Ferret.Hydration.Dapper;
using Ferret.Hydration.Dapper.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Ferret.Hosting.Tests;

public sealed class ReindexHostedServiceTests : IAsyncLifetime
{
    private const string SourceTable  = "reindex_docs";
    private const string SidecarTable = "reindex_docs_search";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage(Environment.GetEnvironmentVariable("FERRET_POSTGRES_IMAGE") ?? "postgres:17-alpine")
        .WithCleanUp(true)
        .Build();

    private string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync() => await _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync();

    [SearchableEntity(Table = SourceTable)]
    [SearchGroup("content", FullTextConfig = "english")]
    public sealed class ReindexDoc : ISearchableEntity<long>
    {
        public long Id { get; init; }

        [Searchable(Backend = SearchBackend.FullText, Group = "content", Weight = 2.0f)]
        public string Title { get; init; } = "";

        [Searchable(Backend = SearchBackend.FullText, Group = "content", Weight = 1.0f)]
        public string Body { get; init; } = "";
    }

    [Fact]
    public async Task Service_drains_pending_job_then_polls()
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await ResetAsync(conn);
        await SeedAsync(conn, 250);

        await using (var ddl = conn.CreateCommand())
        {
            ddl.CommandText = FullTextDdlBuilder.EnsureReindexJobsTable() +
                "TRUNCATE TABLE \"ferret_reindex_jobs\" RESTART IDENTITY;" +
                "INSERT INTO \"ferret_reindex_jobs\" (\"entity\", \"group_name\", \"status\", \"batch_size\") " +
                "VALUES ('reindex_docs', 'content', 'pending', 100);";
            await ddl.ExecuteNonQueryAsync();
        }

        var sc = new ServiceCollection();
        sc.AddLogging();
        sc.AddFerret(opts => opts
            .ScanAssembly(typeof(ReindexDoc).Assembly)
            .UseFullTextSearch(ft => ft.DefaultConfig = "english")
            .UseDapperHydration());

        var connectionString = ConnectionString;
        sc.AddFerretReindexHostedService(o =>
        {
            o.PollInterval = TimeSpan.FromMilliseconds(50);
            o.StaleClaimAfter = TimeSpan.FromMinutes(1);
            o.BatchSizeOverride = 50;
            o.BatchDelayOverride = TimeSpan.Zero;
            o.SessionFactory = (sp, _) =>
            {
                var dialect = sp.GetRequiredService<ISqlDialect>();
                var csb = new NpgsqlConnectionStringBuilder(connectionString) { PersistSecurityInfo = true };
                IFerretSession session = new DapperSession(
                    ct => Task.FromResult<DbConnection>(new NpgsqlConnection(csb.ConnectionString)),
                    dialect);
                return Task.FromResult(session);
            };
        });

        await using var sp = sc.BuildServiceProvider();

        var hosted = sp.GetServices<IHostedService>().OfType<ReindexHostedService>().Single();

        await hosted.StartAsync(CancellationToken.None);

        try
        {
            await WaitForJobDoneAsync(conn, TimeSpan.FromSeconds(30));
        }
        finally
        {
            await hosted.StopAsync(CancellationToken.None);
        }

        await using (var job = conn.CreateCommand())
        {
            job.CommandText = """
                SELECT "status", "last_id"
                FROM "ferret_reindex_jobs"
                ORDER BY "id" DESC LIMIT 1;
                """;
            await using var jr = await job.ExecuteReaderAsync();
            await jr.ReadAsync();
            jr.GetString(0).Should().Be("done");
            jr.GetString(1).Should().Be("250");
        }

        await using var check = conn.CreateCommand();
        check.CommandText = $"""
            SELECT count(*),
                   count(*) FILTER (WHERE content_tsv IS NULL)
            FROM {SidecarTable};
            """;
        await using var reader = await check.ExecuteReaderAsync();
        await reader.ReadAsync();
        reader.GetInt64(0).Should().Be(250);
        reader.GetInt64(1).Should().Be(0);
    }

    private static async Task WaitForJobDoneAsync(NpgsqlConnection conn, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT "status" FROM "ferret_reindex_jobs" ORDER BY "id" DESC LIMIT 1;
                """;
            var status = (string?)await cmd.ExecuteScalarAsync();
            if (status == "done")
                return;
            await Task.Delay(100);
        }

        throw new TimeoutException("Reindex job did not reach 'done' within the timeout.");
    }

    private static async Task ResetAsync(NpgsqlConnection conn)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            DROP TABLE IF EXISTS {SidecarTable} CASCADE;
            DROP TABLE IF EXISTS {SourceTable} CASCADE;
            CREATE TABLE {SourceTable} (
                id bigint PRIMARY KEY,
                title text NOT NULL,
                body  text NOT NULL
            );
            CREATE TABLE {SidecarTable} (
                id bigint PRIMARY KEY REFERENCES {SourceTable}(id) ON DELETE CASCADE,
                content_tsv tsvector,
                updated_at timestamptz NOT NULL DEFAULT now()
            );
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task SeedAsync(NpgsqlConnection conn, int rows)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {SourceTable} (id, title, body)
            SELECT g, 'title ' || g, 'body ' || g
            FROM generate_series(1, {rows}) AS g;
            """;
        await cmd.ExecuteNonQueryAsync();
    }
}
