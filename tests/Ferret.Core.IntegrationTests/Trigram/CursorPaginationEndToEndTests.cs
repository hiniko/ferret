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
public class CursorPaginationEndToEndTests
{
    private readonly PostgresFixture _fx;

    public CursorPaginationEndToEndTests(PostgresFixture fx) => _fx = fx;

    [SearchableEntity(Table = "widgets")]
    public sealed class Widget : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }
        [Searchable] public string Name { get; init; } = "";
        [Searchable(Weight = 2.0f)] public string Sku { get; init; } = "";
    }

    [Fact]
    public async Task Cursor_forward_then_backward_returns_consistent_pages()
    {
        await SeedManyAsync(50);

        var sp = BuildServices();
        var engine = sp.GetRequiredService<IFerretEngine>();
        var dialect = sp.GetRequiredService<ISqlDialect>();

        await using var session = new DapperSession(
            ct => Task.FromResult<DbConnection>(new NpgsqlConnection(_fx.ConnectionString)),
            dialect);

        var page1 = await engine.SearchCursorAsync<Widget, Guid>(session, new PagedQuery<Widget, Guid>
        {
            Mode = PaginationMode.Cursor,
            Limit = 10,
        });

        page1.Items.Should().HaveCount(10);
        page1.HasMore.Should().BeTrue();
        page1.NextCursor.Should().NotBeNull();

        var page2 = await engine.SearchCursorAsync<Widget, Guid>(session, new PagedQuery<Widget, Guid>
        {
            Mode = PaginationMode.Cursor,
            Limit = 10,
            Cursor = page1.NextCursor,
            CursorDirection = CursorDirection.Forward,
        });

        page2.Items.Should().HaveCount(10);
        page2.Items.Select(w => w.Id).Should().NotIntersectWith(page1.Items.Select(w => w.Id));
        page2.PrevCursor.Should().NotBeNull();

        var backToPage1 = await engine.SearchCursorAsync<Widget, Guid>(session, new PagedQuery<Widget, Guid>
        {
            Mode = PaginationMode.Cursor,
            Limit = 10,
            Cursor = page2.PrevCursor,
            CursorDirection = CursorDirection.Backward,
        });

        backToPage1.Items.Select(w => w.Id).Should().Equal(page1.Items.Select(w => w.Id));
    }

    [Fact]
    public async Task Cursor_with_mismatched_sort_fingerprint_throws()
    {
        await SeedManyAsync(15);

        var sp = BuildServices();
        var engine = sp.GetRequiredService<IFerretEngine>();
        var dialect = sp.GetRequiredService<ISqlDialect>();

        await using var session = new DapperSession(
            ct => Task.FromResult<DbConnection>(new NpgsqlConnection(_fx.ConnectionString)),
            dialect);

        var page1 = await engine.SearchCursorAsync<Widget, Guid>(session, new PagedQuery<Widget, Guid>
        {
            Mode = PaginationMode.Cursor,
            Limit = 5,
            Sort = [new SortClause { Field = "Name", Direction = SortDirection.Ascending }],
        });

        page1.NextCursor.Should().NotBeNull();

        var act = async () => await engine.SearchCursorAsync<Widget, Guid>(session, new PagedQuery<Widget, Guid>
        {
            Mode = PaginationMode.Cursor,
            Limit = 5,
            Cursor = page1.NextCursor,
            CursorDirection = CursorDirection.Forward,
            Sort = [new SortClause { Field = "Name", Direction = SortDirection.Descending }],
        });

        await act.Should().ThrowAsync<InvalidCursorException>();
    }

    private async Task SeedManyAsync(int count)
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("TRUNCATE widgets");

        var rows = Enumerable.Range(0, count).Select(i => new
        {
            Id = Guid.NewGuid(),
            Name = $"Widget {i:D3}",
            Sku = $"SKU-{i:D4}",
        }).ToArray();

        await conn.ExecuteAsync(
            "INSERT INTO widgets (id, name, sku) VALUES (@Id, @Name, @Sku)",
            rows);
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
