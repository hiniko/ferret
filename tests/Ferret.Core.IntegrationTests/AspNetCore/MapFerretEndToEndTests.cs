using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Ferret.Abstractions;
using Ferret.AspNetCore;
using Ferret.AspNetCore.DependencyInjection;
using Ferret.Core.IntegrationTests.Fixtures;
using Ferret.EntityFrameworkCore.DependencyInjection;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Xunit;

namespace Ferret.Core.IntegrationTests.AspNetCore;

[Trait("Category", "Integration")]
[Collection("postgres")]
public sealed class MapFerretEndToEndTests
{
    private readonly PostgresFixture _fx;

    public MapFerretEndToEndTests(PostgresFixture fx) => _fx = fx;

    [SearchableEntity(Table = "widgets")]
    public sealed class Product : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }
        [Searchable, Filterable, Sortable] public string Name { get; init; } = "";
        [Searchable, Filterable, Sortable] public string Sku { get; init; } = "";
    }

    public sealed class ProductContext : DbContext
    {
        public ProductContext(DbContextOptions<ProductContext> opts) : base(opts) { }

        public DbSet<Product> Products => Set<Product>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Product>(e =>
            {
                e.ToTable("widgets");
                e.HasKey(p => p.Id);
                e.Property(p => p.Id).HasColumnName("id");
                e.Property(p => p.Name).HasColumnName("name");
                e.Property(p => p.Sku).HasColumnName("sku");
            });
        }
    }

    private sealed class Factory : WebApplicationFactory<Product>
    {
        public required string ConnectionString { get; init; }
        public required FerretEndpointPaginationMode Mode { get; init; }

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseContentRoot(AppContext.BaseDirectory);
            builder.ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddFerretAspNetCore();
                        services.AddDbContext<ProductContext>(o => o.UseNpgsql(ConnectionString));
                        services.AddFerret(opts => opts
                            .ScanAssembly(typeof(Product).Assembly)
                            .UseTrigramSearch());
                        services.AddFerretEntityFrameworkQueryService<ProductContext>();
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                            endpoints.MapFerret<Product, Guid>("/api/products", o => o.Pagination = Mode));
                    });
            });

            var host = builder.Build();
            host.Start();
            return host;
        }

        protected override IHostBuilder CreateHostBuilder()
            => Host.CreateDefaultBuilder();
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
                new { Id = Guid.NewGuid(), Name = "Green Widget", Sku = "GRN-002" },
            });
    }

    [Fact]
    public async Task Offset_endpoint_returns_real_paged_rows_from_postgres()
    {
        await Seed();
        await using var factory = new Factory
        {
            ConnectionString = _fx.ConnectionString,
            Mode = FerretEndpointPaginationMode.Offset,
        };
        var client = factory.CreateClient();

        var response = await client.GetAsync(
            "/api/products?filter=Name:contains:Widget&sort=Name:asc&limit=2&count=true");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var items = root.GetProperty("items");
        items.GetArrayLength().Should().Be(2);
        items[0].GetProperty("name").GetString().Should().Be("Blue Widget");
        items[1].GetProperty("name").GetString().Should().Be("Green Widget");
        root.GetProperty("totalCount").GetInt32().Should().Be(3);
        root.GetProperty("hasMore").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Offset_endpoint_search_term_filters_rows()
    {
        await Seed();
        await using var factory = new Factory
        {
            ConnectionString = _fx.ConnectionString,
            Mode = FerretEndpointPaginationMode.Offset,
        };
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/products?q=Red&fields=Name");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var names = result.GetProperty("items")
            .EnumerateArray()
            .Select(e => e.GetProperty("name").GetString())
            .ToArray();

        names.Should().ContainSingle().Which.Should().Be("Red Widget");
    }

    [Fact]
    public async Task Cursor_endpoint_pages_forward_with_real_cursor_token()
    {
        await Seed();
        await using var factory = new Factory
        {
            ConnectionString = _fx.ConnectionString,
            Mode = FerretEndpointPaginationMode.Cursor,
        };
        var client = factory.CreateClient();

        var firstResponse = await client.GetAsync("/api/products?sort=Name:asc&limit=2");
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var first = await firstResponse.Content.ReadFromJsonAsync<JsonElement>();
        var firstNames = first.GetProperty("items")
            .EnumerateArray()
            .Select(e => e.GetProperty("name").GetString())
            .ToArray();
        firstNames.Should().Equal("Blue Widget", "Green Widget");
        first.GetProperty("hasMore").GetBoolean().Should().BeTrue();

        var nextCursor = first.GetProperty("nextCursor").GetString();
        nextCursor.Should().NotBeNullOrEmpty();

        var secondResponse = await client.GetAsync(
            $"/api/products?sort=Name:asc&limit=2&after={Uri.EscapeDataString(nextCursor!)}");
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await secondResponse.Content.ReadFromJsonAsync<JsonElement>();
        var secondNames = second.GetProperty("items")
            .EnumerateArray()
            .Select(e => e.GetProperty("name").GetString())
            .ToArray();
        secondNames.Should().Equal("Red Widget");
    }
}
