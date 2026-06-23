using System.Data.Common;
using Dapper;
using Ferret.Abstractions;
using Ferret.Core.IntegrationTests.Fixtures;
using Ferret.Hydration.Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Ferret.Core.IntegrationTests;

[Collection("postgres")]
public class FilterOperatorsEndToEndTests
{
    private readonly PostgresFixture _fx;

    public FilterOperatorsEndToEndTests(PostgresFixture fx) => _fx = fx;

    [SearchableEntity(Table = "widgets")]
    public sealed class Widget : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }
        [Filterable, Sortable] public string Name { get; init; } = "";
        [Filterable] public string Sku { get; init; } = "";
    }

    [Fact]
    public async Task In_filter_returns_only_matching_rows()
    {
        await Seed();
        var engine = await Engine();

        var result = await engine.SearchOffsetAsync<Widget, Guid>(NewSession(), new PagedQuery<Widget, Guid>
        {
            Mode = PaginationMode.Offset,
            Limit = 50,
            Page = 0,
            Filter = [new FilterClause { Field = "Sku", Operator = FilterOperator.In, Value = "BLUE-001,GRN-002" }],
        });

        result.Items.Select(w => w.Sku).Should().BeEquivalentTo(["BLUE-001", "GRN-002"]);
    }

    [Theory]
    [InlineData(FilterOperator.NotEquals,          "Blue Widget", new[] { "Red Widget", "Greenish Hue" })]
    [InlineData(FilterOperator.GreaterThanOrEqual, "Red Widget",  new[] { "Red Widget" })]
    [InlineData(FilterOperator.LessThanOrEqual,    "Blue Widget", new[] { "Blue Widget" })]
    public async Task Comparison_filters_apply_expected_predicate(FilterOperator op, string value, string[] expected)
    {
        await Seed();
        var engine = await Engine();

        var result = await engine.SearchOffsetAsync<Widget, Guid>(NewSession(), new PagedQuery<Widget, Guid>
        {
            Mode = PaginationMode.Offset,
            Limit = 50,
            Page = 0,
            Filter = [new FilterClause { Field = "Name", Operator = op, Value = value }],
        });

        result.Items.Select(w => w.Name).Should().BeEquivalentTo(expected);
    }

    private DapperSession NewSession()
    {
        var dialect = new Core.Sql.PostgresDialect();
        return new DapperSession(
            ct => Task.FromResult<DbConnection>(new NpgsqlConnection(_fx.ConnectionString)),
            dialect);
    }

    private async Task<IFerretEngine> Engine()
    {
        var sc = new ServiceCollection();
        sc.AddLogging();
        sc.AddFerret(opts => opts
            .ScanAssembly(typeof(Widget).Assembly)
            .UseTrigramSearch()
            .UseDapperHydration());
        var sp = sc.BuildServiceProvider();
        await Task.CompletedTask;
        return sp.GetRequiredService<IFerretEngine>();
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
}
