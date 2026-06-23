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
public class SearchJoinEndToEndTests : IAsyncLifetime
{
    private readonly PostgresFixture _fx;

    public SearchJoinEndToEndTests(PostgresFixture fx) => _fx = fx;

    [SearchableEntity(Table = "orders")]
    public sealed class Order : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }
        [Searchable] public string Customer { get; init; } = "";

        [SearchJoin(ForeignKey = "order_id")]
        public IReadOnlyList<OrderItem> Items { get; init; } = [];
    }

    public sealed class OrderItem
    {
        public Guid Id { get; init; }
        public Guid OrderId { get; init; }
        [Searchable] public string Description { get; init; } = "";

        [SearchJoin(ForeignKey = "item_id")]
        public IReadOnlyList<OrderItemTag> Tags { get; init; } = [];
    }

    public sealed class OrderItemTag
    {
        public Guid Id { get; init; }
        public Guid ItemId { get; init; }
        [Searchable] public string Label { get; init; } = "";
    }

    public async Task InitializeAsync()
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("""
            DROP TABLE IF EXISTS order_item_tags;
            DROP TABLE IF EXISTS order_items;
            DROP TABLE IF EXISTS orders;
            CREATE TABLE orders (
                id uuid PRIMARY KEY,
                customer text NOT NULL
            );
            CREATE TABLE order_items (
                id uuid PRIMARY KEY,
                order_id uuid NOT NULL REFERENCES orders(id),
                description text NOT NULL
            );
            CREATE TABLE order_item_tags (
                id uuid PRIMARY KEY,
                item_id uuid NOT NULL REFERENCES order_items(id),
                label text NOT NULL
            );
            CREATE INDEX ON orders             USING gist (customer    gist_trgm_ops);
            CREATE INDEX ON order_items        USING gist (description gist_trgm_ops);
            CREATE INDEX ON order_item_tags    USING gist (label       gist_trgm_ops);
        """);
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("DROP TABLE IF EXISTS order_item_tags; DROP TABLE IF EXISTS order_items; DROP TABLE IF EXISTS orders");
    }

    [Fact]
    public async Task Search_matches_via_child_text_returns_parent_rows()
    {
        var (o1, o2) = await Seed();
        var engine = BuildEngine();

        var result = await engine.SearchOffsetAsync<Order, Guid>(NewSession(), new PagedQuery<Order, Guid>
        {
            Mode = PaginationMode.Offset,
            Search = "umbrella",
            Limit = 25,
            Page = 0,
        });

        result.Items.Select(o => o.Id).Should().Contain(o1).And.NotContain(o2);
    }

    [Fact]
    public async Task Search_matches_via_grandchild_text_returns_parent_rows()
    {
        var (o1, o2) = await Seed();
        var engine = BuildEngine();

        var result = await engine.SearchOffsetAsync<Order, Guid>(NewSession(), new PagedQuery<Order, Guid>
        {
            Mode = PaginationMode.Offset,
            Search = "fragile",
            Limit = 25,
            Page = 0,
        });

        result.Items.Select(o => o.Id).Should().Contain(o1).And.NotContain(o2);
    }

    [Fact]
    public async Task Search_matches_via_parent_text_still_works_alongside_joins()
    {
        var (_, o2) = await Seed();
        var engine = BuildEngine();

        var result = await engine.SearchOffsetAsync<Order, Guid>(NewSession(), new PagedQuery<Order, Guid>
        {
            Mode = PaginationMode.Offset,
            Search = "Alice",
            Limit = 25,
            Page = 0,
        });

        result.Items.Select(o => o.Id).Should().Contain(o2);
    }

    private async Task<(Guid OrderWithUmbrellaItem, Guid OrderWithAliceCustomer)> Seed()
    {
        var o1 = Guid.NewGuid();
        var o2 = Guid.NewGuid();
        var i1 = Guid.NewGuid();

        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            "INSERT INTO orders (id, customer) VALUES (@Id, @Customer)",
            new[]
            {
                new { Id = o1, Customer = "Bob" },
                new { Id = o2, Customer = "Alice" },
            });
        await conn.ExecuteAsync(
            "INSERT INTO order_items (id, order_id, description) VALUES (@Id, @OrderId, @Description)",
            new[]
            {
                new { Id = i1, OrderId = o1, Description = "Black umbrella, large" },
                new { Id = Guid.NewGuid(), OrderId = o2, Description = "Notebook, A5" },
            });
        await conn.ExecuteAsync(
            "INSERT INTO order_item_tags (id, item_id, label) VALUES (@Id, @ItemId, @Label)",
            new[]
            {
                new { Id = Guid.NewGuid(), ItemId = i1, Label = "fragile" },
            });
        return (o1, o2);
    }

    private DapperSession NewSession()
    {
        var dialect = new Core.Sql.PostgresDialect();
        return new DapperSession(
            ct => Task.FromResult<DbConnection>(new NpgsqlConnection(_fx.ConnectionString)),
            dialect);
    }

    private IFerretEngine BuildEngine()
    {
        var sc = new ServiceCollection();
        sc.AddLogging();
        sc.AddFerret(opts => opts
            .ScanAssembly(typeof(Order).Assembly)
            .UseTrigramSearch()
            .UseDapperHydration());
        return sc.BuildServiceProvider().GetRequiredService<IFerretEngine>();
    }
}
