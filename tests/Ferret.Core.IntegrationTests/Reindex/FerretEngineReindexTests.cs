using System.Data.Common;
using Ferret.Abstractions;
using Ferret.Core.Engine;
using Ferret.Core.IntegrationTests.Fixtures;
using Ferret.Hydration.Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Ferret.Core.IntegrationTests.Reindex;

[Collection("postgres")]
public class FerretEngineReindexTests
{
    private readonly PostgresFixture _fx;

    public FerretEngineReindexTests(PostgresFixture fx) => _fx = fx;

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
    public async Task ReindexAsync_populates_sidecar()
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await ReindexTestSchema.ResetAsync(conn);
        await ReindexTestSchema.SeedAsync(conn, 250);

        var sc = new ServiceCollection();
        sc.AddLogging();
        sc.AddFerret(opts => opts
            .ScanAssembly(typeof(ReindexDoc).Assembly)
            .UsePostgres()
            .UseFullTextSearch(ft => ft.DefaultConfig = "english")
            .UseDapperHydration());
        await using var sp = sc.BuildServiceProvider();

        var engine = sp.GetRequiredService<IFerretEngine>();
        var dialect = sp.GetRequiredService<ISqlDialect>();

        var csb = new NpgsqlConnectionStringBuilder(_fx.ConnectionString) { PersistSecurityInfo = true };
        await using var session = new DapperSession(
            ct => Task.FromResult<DbConnection>(new NpgsqlConnection(csb.ConnectionString)),
            dialect);

        await engine.ReindexAsync<ReindexDoc>(
            session,
            "content",
            new ReindexOptions { BatchSize = 100 },
            CancellationToken.None);

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

    [Fact]
    public async Task ReindexAsync_throws_when_group_absent()
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await ReindexTestSchema.ResetAsync(conn);
        await ReindexTestSchema.SeedAsync(conn, 1);

        var sc = new ServiceCollection();
        sc.AddLogging();
        sc.AddFerret(opts => opts
            .ScanAssembly(typeof(ReindexDoc).Assembly)
            .UsePostgres()
            .UseFullTextSearch(ft => ft.DefaultConfig = "english")
            .UseDapperHydration());
        await using var sp = sc.BuildServiceProvider();

        var engine = sp.GetRequiredService<IFerretEngine>();
        var dialect = sp.GetRequiredService<ISqlDialect>();

        var csb = new NpgsqlConnectionStringBuilder(_fx.ConnectionString) { PersistSecurityInfo = true };
        await using var session = new DapperSession(
            ct => Task.FromResult<DbConnection>(new NpgsqlConnection(csb.ConnectionString)),
            dialect);

        var act = () => engine.ReindexAsync<ReindexDoc>(
            session,
            "does_not_exist",
            new ReindexOptions { BatchSize = 100 },
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
