using System.Data.Common;
using Dapper;
using Ferret.Abstractions;
using Ferret.Core.IntegrationTests.Fixtures;
using Ferret.Hydration.Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Ferret.Core.IntegrationTests.Trigram;

[Collection("postgres")]
public class TrigramSearchEndToEndTests
{
    private readonly PostgresFixture _fx;

    public TrigramSearchEndToEndTests(PostgresFixture fx) => _fx = fx;

    [SearchableEntity(Table = "widgets")]
    public sealed class Widget : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }
        [Searchable, Filterable] public string Name { get; init; } = "";
        [Searchable(Weight = 2.0f), Filterable] public string Sku { get; init; } = "";
    }

    [Fact]
    public async Task Search_finds_close_matches_via_trigram()
    {
        await Seed();

        var sp = BuildServices();
        var engine = sp.GetRequiredService<IFerretEngine>();
        var dialect = sp.GetRequiredService<ISqlDialect>();

        await using var session = new DapperSession(
            ct => Task.FromResult<DbConnection>(new NpgsqlConnection(_fx.ConnectionString)),
            dialect);

        var result = await engine.SearchOffsetAsync<Widget, Guid>(session, new PagedQuery<Widget, Guid>
        {
            Mode = PaginationMode.Offset,
            Search = "blue widge",
            Limit = 10,
            Page = null,
        });

        result.Items.Should().NotBeEmpty();
        result.Items.First().Name.ToLower().Should().Contain("blue");
        result.Page.Should().Be(0);
        result.TotalCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Search_combined_with_filter_restricts_results()
    {
        await Seed();

        var sp = BuildServices();
        var engine = sp.GetRequiredService<IFerretEngine>();
        var dialect = sp.GetRequiredService<ISqlDialect>();

        await using var session = new DapperSession(
            ct => Task.FromResult<DbConnection>(new NpgsqlConnection(_fx.ConnectionString)),
            dialect);

        // Without filter: "widget" matches Blue and Red.
        var unfiltered = await engine.SearchOffsetAsync<Widget, Guid>(session, new PagedQuery<Widget, Guid>
        {
            Mode = PaginationMode.Offset,
            Search = "widget",
            Limit = 10,
        });
        unfiltered.Items.Select(w => w.Name).Should().Contain(new[] { "Blue Widget", "Red Widget" });

        // With filter on Sku: only Blue.
        var filtered = await engine.SearchOffsetAsync<Widget, Guid>(session, new PagedQuery<Widget, Guid>
        {
            Mode = PaginationMode.Offset,
            Search = "widget",
            Limit = 10,
            Filter = [new FilterClause { Field = nameof(Widget.Sku), Operator = FilterOperator.Equals, Value = "BLUE-001" }],
        });
        filtered.Items.Should().ContainSingle(w => w.Name == "Blue Widget");
    }

    private async Task Seed()
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("TRUNCATE widgets");
        await conn.ExecuteAsync(
            "INSERT INTO widgets (id, name, sku) VALUES (@Id, @Name, @Sku)",
            new[]
            {
                new { Id = Guid.NewGuid(), Name = "Blue Widget",  Sku = "BLUE-001" },
                new { Id = Guid.NewGuid(), Name = "Red Widget",   Sku = "RED-001" },
                new { Id = Guid.NewGuid(), Name = "Greenish Hue", Sku = "GRN-002" },
            });
    }

    private ServiceProvider BuildServices()
    {
        var sc = new ServiceCollection();
        sc.AddLogging();
        sc.AddFerret(opts => opts
            .ScanAssembly(typeof(Widget).Assembly)
            .UsePostgres()
            .UseTrigramSearch()
            .UseDapperHydration());
        return sc.BuildServiceProvider();
    }
}
