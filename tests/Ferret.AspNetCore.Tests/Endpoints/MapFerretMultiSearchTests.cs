using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ferret.Abstractions.Models;
using Ferret.Abstractions.Querying;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ferret.AspNetCore.Tests.Endpoints;

public class MapFerretMultiSearchTests : IClassFixture<MapFerretMultiSearchTests.Factory>
{
    private readonly Factory _factory;

    public MapFerretMultiSearchTests(Factory factory) => _factory = factory;

    public sealed class Author
    {
        public Author() { }

        public Author(string name) => Name = name;

        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public bool Hidden { get; set; }
    }

    public sealed class Book
    {
        public string Title { get; set; } = "";
        public string Isbn { get; set; } = "";
    }

    [Fact]
    public async Task Happy_path_returns_grouped_hits_with_projected_fields_only()
    {
        _factory.Service.Reset();
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/search?q=moby");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var groups = doc.RootElement.GetProperty("groups");
        groups.GetArrayLength().Should().Be(2);
        groups[0].GetProperty("entityType").GetString().Should().Be("authors");

        var authorHit = groups[0].GetProperty("hits")[0];
        authorHit.GetProperty("name").GetString().Should().Be("Moby Dickens");
        authorHit.GetProperty("email").GetString().Should().Be("moby@sea.org");
        authorHit.EnumerateObject().Should().HaveCount(2, "only projected members go on the wire");

        var bookHit = groups[1].GetProperty("hits")[0];
        bookHit.GetProperty("title").GetString().Should().Be("Moby-Dick");
        bookHit.EnumerateObject().Should().HaveCount(1);
    }

    [Fact]
    public async Task Query_flows_search_types_and_limit()
    {
        _factory.Service.Reset();
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/search?q=moby&types=authors,books&limit=3");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var captured = _factory.Service.LastQuery;
        captured.Should().NotBeNull();
        captured!.Search.Should().Be("moby");
        captured.PerEntityLimit.Should().Be(3);
        captured.Types.Should().Equal("authors", "books");
        captured.Entities.Should().HaveCount(2);
    }

    [Fact]
    public async Task Missing_q_returns_400()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/search");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Limit_over_max_returns_400_with_max_in_detail()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/search?q=x&limit=9999");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("detail").GetString().Should().Contain("10");
    }

    [Fact]
    public async Task Unknown_type_maps_engine_argument_exception_to_400()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/search?q=x&types=nope");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("detail").GetString().Should().Contain("nope");
    }

    [Fact]
    public async Task Hit_keys_follow_the_apps_json_naming_policy()
    {
        await using var app = await CreateAppAsync(
            b => b.Services.ConfigureHttpJsonOptions(o =>
                o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower),
            o => o.Entity<Author>(a => new Author { Name = a.Name, DisplayName = a.DisplayName }));

        var response = await app.GetTestClient().GetAsync("/api/search?q=moby");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var group = doc.RootElement.GetProperty("groups")[0];
        group.GetProperty("entity_type").GetString().Should().Be("authors");
        var hit = group.GetProperty("hits")[0];
        hit.GetProperty("name").GetString().Should().Be("Moby Dickens");
        hit.GetProperty("display_name").GetString().Should().Be("Moby D.");
    }

    [Fact]
    public async Task Custom_shaper_owns_the_response()
    {
        await using var app = await CreateAppAsync(null, o => o
            .Entity<Author>(a => new Author { Name = a.Name })
            .ShapeResponse(r => new { total = r.Groups.Count, names = r.HitsOf<Author>().Select(a => a.Name) }));

        var response = await app.GetTestClient().GetAsync("/api/search?q=moby");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("total").GetInt32().Should().Be(1);
        doc.RootElement.GetProperty("names")[0].GetString().Should().Be("Moby Dickens");
    }

    private static async Task<WebApplication> CreateAppAsync(
        Action<WebApplicationBuilder>? configureBuilder,
        Action<MultiSearchEndpointOptions> configureOptions)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();
        builder.Services.AddSingleton<IFerretQueryService>(new FakeMultiSearchService());
        configureBuilder?.Invoke(builder);
        var app = builder.Build();
        app.MapFerretMultiSearch("/api/search", configureOptions);
        await app.StartAsync();
        return app;
    }

    [Fact]
    public void Ctor_form_projection_is_rejected_at_map_time()
    {
        var options = new MultiSearchEndpointOptions();
        FluentActions.Invoking(() => options.Entity<Author>(a => new Author(a.Name)))
            .Should().Throw<InvalidOperationException>().WithMessage("*member-init*");
    }

    public sealed class FakeMultiSearchService : IFerretQueryService
    {
        public MultiSearchQuery? LastQuery { get; private set; }

        public void Reset() => LastQuery = null;

        public Task<MultiSearchResult> MultiSearchAsync(
            MultiSearchQuery query,
            CancellationToken ct = default)
        {
            if (query.Types?.Contains("nope") == true)
            {
                throw new ArgumentException("Unknown multisearch type(s): nope. Valid types: authors, books.");
            }
            LastQuery = query;
            var groups = new List<MultiSearchGroup>();
            if (query.Entities.Any(e => e.EntityType == typeof(Author)))
            {
                groups.Add(new MultiSearchGroup
                {
                    EntityType = "authors",
                    Hits = [new Author { Name = "Moby Dickens", Email = "moby@sea.org", DisplayName = "Moby D." }],
                });
            }
            if (query.Entities.Any(e => e.EntityType == typeof(Book)))
            {
                groups.Add(new MultiSearchGroup
                {
                    EntityType = "books",
                    Hits = [new Book { Title = "Moby-Dick" }],
                });
            }
            return Task.FromResult(new MultiSearchResult { Groups = groups });
        }

        public Task<OffsetResult<T>> SearchOffsetAsync<T, TKey>(
            PagedQuery<T, TKey> query,
            CancellationToken ct = default)
            where T : class
            where TKey : notnull
            => throw new NotSupportedException();

        public Task<CursorResult<T>> SearchCursorAsync<T, TKey>(
            PagedQuery<T, TKey> query,
            CancellationToken ct = default)
            where T : class
            where TKey : notnull
            => throw new NotSupportedException();
    }

    public sealed class Factory : IAsyncLifetime
    {
        private WebApplication? _app;

        public FakeMultiSearchService Service { get; } = new();

        public async Task InitializeAsync()
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddRouting();
            builder.Services.AddSingleton<IFerretQueryService>(Service);

            _app = builder.Build();
            _app.MapFerretMultiSearch("/api/search", o => o
                .WithLimits(defaultPerEntityLimit: 5, maxPerEntityLimit: 10)
                .Entity<Author>(a => new Author { Name = a.Name, Email = a.Email })
                .Entity<Book>(b => new Book { Title = b.Title }));
            await _app.StartAsync();
        }

        public HttpClient CreateClient() => _app!.GetTestClient();

        public async Task DisposeAsync()
        {
            if (_app is not null)
            {
                await _app.DisposeAsync();
            }
        }
    }
}
