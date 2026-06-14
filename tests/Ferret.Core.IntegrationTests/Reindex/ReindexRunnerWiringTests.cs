using System.Data.Common;
using Ferret.Abstractions;
using Ferret.Core.Backends.FullText;
using Ferret.Core.Engine.Reindex;
using Ferret.Core.IntegrationTests.Fixtures;
using Ferret.Hydration.Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Ferret.Core.IntegrationTests.Reindex;

[Collection("postgres")]
public class ReindexRunnerWiringTests
{
    private readonly PostgresFixture _fx;

    public ReindexRunnerWiringTests(PostgresFixture fx) => _fx = fx;

    [SearchableEntity(Table = ReindexTestSchema.SourceTable)]
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
    public async Task AddFerret_registers_runner_that_drains_pending_job_to_done()
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await ReindexTestSchema.ResetAsync(conn);
        await ReindexTestSchema.SeedAsync(conn, 250);

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
            .UsePostgres()
            .UseFullTextSearch(ft => ft.DefaultConfig = "english")
            .UseDapperHydration());
        await using var sp = sc.BuildServiceProvider();

        var runner = sp.GetRequiredService<IReindexRunner>();
        var dialect = sp.GetRequiredService<ISqlDialect>();

        var csb = new NpgsqlConnectionStringBuilder(_fx.ConnectionString) { PersistSecurityInfo = true };
        await using var session = new DapperSession(
            ct => Task.FromResult<DbConnection>(new NpgsqlConnection(csb.ConnectionString)),
            dialect);

        var processed = await runner.DrainAsync(session, CancellationToken.None);
        processed.Should().Be(1);

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
            FROM {ReindexTestSchema.SidecarTable};
            """;
        await using var reader = await check.ExecuteReaderAsync();
        await reader.ReadAsync();
        reader.GetInt64(0).Should().Be(250);
        reader.GetInt64(1).Should().Be(0);
    }
}
