using System.Data.Common;
using Dapper;
using Ferret.Abstractions;
using Ferret.Core.IntegrationTests.Fixtures;
using Ferret.Hydration.Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Ferret.Core.IntegrationTests.Search;

[Collection("postgres")]
public class CandidateKeysEndToEndTests
{
    private readonly PostgresFixture _fx;

    public CandidateKeysEndToEndTests(PostgresFixture fx) => _fx = fx;

    [SearchableEntity(Table = "widgets")]
    public sealed class Widget : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }
        [Searchable] public string Name { get; init; } = "";
        [Filterable] public string Sku { get; init; } = "";
    }

    private static readonly Guid IdBlue  = Guid.Parse("00000002-0000-0000-0000-000000000001");
    private static readonly Guid IdRed   = Guid.Parse("00000002-0000-0000-0000-000000000002");
    private static readonly Guid IdGreen = Guid.Parse("00000002-0000-0000-0000-000000000003");

    [Fact]
    public async Task CandidateKeys_restricts_search_to_listed_rows()
    {
        await SeedAsync();
        var (engine, session) = await EngineAsync();

        var result = await engine.SearchOffsetAsync<Widget, Guid>(session, new PagedQuery<Widget, Guid>
        {
            Mode = PaginationMode.Offset,
            Search = "widget",
            Limit = 50,
            RequestTotalCount = true,
            CandidateKeys = [IdBlue, IdGreen],
        });

        result.Items.Select(w => w.Id).Should().BeEquivalentTo([IdBlue, IdGreen]);
        result.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task Empty_CandidateKeys_short_circuits_to_empty_result()
    {
        await SeedAsync();
        var (engine, session) = await EngineAsync();

        var result = await engine.SearchOffsetAsync<Widget, Guid>(session, new PagedQuery<Widget, Guid>
        {
            Mode = PaginationMode.Offset,
            Search = "widget",
            Limit = 50,
            RequestTotalCount = true,
            CandidateKeys = [],
        });

        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task CandidateKeys_intersect_with_filter_clauses()
    {
        await SeedAsync();
        var (engine, session) = await EngineAsync();

        var result = await engine.SearchOffsetAsync<Widget, Guid>(session, new PagedQuery<Widget, Guid>
        {
            Mode = PaginationMode.Offset,
            Search = "widget",
            Limit = 50,
            RequestTotalCount = true,
            // Candidates allow blue+red; filter allows red+green → intersection is red only.
            CandidateKeys = [IdBlue, IdRed],
            Filter = [new FilterClause { Field = "Sku", Operator = FilterOperator.In, Value = "RED-001,GRN-002" }],
        });

        result.Items.Select(w => w.Id).Should().BeEquivalentTo([IdRed]);
    }

    private async Task<(IFerretEngine Engine, DapperSession Session)> EngineAsync()
    {
        var sc = new ServiceCollection();
        sc.AddLogging();
        sc.AddFerret(opts => opts
            .ScanAssembly(typeof(Widget).Assembly)
            .UseTrigramSearch()
            .UseDapperHydration());
        var sp = sc.BuildServiceProvider();
        var dialect = sp.GetRequiredService<ISqlDialect>();
        var session = new DapperSession(
            ct => Task.FromResult<DbConnection>(new NpgsqlConnection(_fx.ConnectionString)),
            dialect);
        await Task.CompletedTask;
        return (sp.GetRequiredService<IFerretEngine>(), session);
    }

    private async Task SeedAsync()
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("TRUNCATE widgets");
        await conn.ExecuteAsync(
            "INSERT INTO widgets (id, name, sku) VALUES (@Id, @Name, @Sku)",
            new[]
            {
                new { Id = IdBlue,  Name = "Blue Widget",  Sku = "BLUE-001" },
                new { Id = IdRed,   Name = "Red Widget",   Sku = "RED-001" },
                new { Id = IdGreen, Name = "Green Widget", Sku = "GRN-002" },
            });
    }
}
