using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ferret.Abstractions.Models;
using Ferret.Abstractions.Querying;
using Ferret.AspNetCore;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace Ferret.AspNetCore.Tests.Endpoints;

public class MapFerretOffsetTests : IClassFixture<MapFerretOffsetTests.Factory>
{
    private readonly Factory _factory;

    public MapFerretOffsetTests(Factory factory) => _factory = factory;

    [Fact]
    public async Task Offset_happy_path_returns_camelCase_json()
    {
        _factory.Service.Reset();
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/products");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("items").GetArrayLength().Should().Be(1);
        root.GetProperty("items")[0].GetProperty("name").GetString().Should().Be("Widget");
        root.GetProperty("limit").GetInt32().Should().Be(25);
        root.GetProperty("page").GetInt32().Should().Be(2);
        root.GetProperty("totalCount").GetInt32().Should().Be(7);
        root.GetProperty("hasMore").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Offset_flows_all_parameters_into_query()
    {
        _factory.Service.Reset();
        var client = _factory.CreateClient();

        var response = await client.GetAsync(
            "/api/products?q=hammer&fields=name&filter=category:eq:tools&filter=price:gt:5&sort=name:desc&limit=10&page=3&count=false");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var captured = _factory.Service.LastQuery;
        captured.Should().NotBeNull();
        captured!.Search.Should().Be("hammer");
        captured.SearchFields.Should().ContainSingle().Which.Should().Be("name");
        captured.Filter.Should().HaveCount(2);
        captured.Filter[0].Field.Should().Be("category");
        captured.Filter[0].Operator.Should().Be(FilterOperator.Equals);
        captured.Filter[1].Field.Should().Be("price");
        captured.Filter[1].Operator.Should().Be(FilterOperator.GreaterThan);
        captured.Sort.Should().ContainSingle();
        captured.Sort[0].Field.Should().Be("name");
        captured.Sort[0].Direction.Should().Be(SortDirection.Descending);
        captured.Limit.Should().Be(10);
        captured.Page.Should().Be(3);
        captured.RequestTotalCount.Should().BeFalse();
    }

    [Fact]
    public async Task Offset_limit_over_max_returns_400_problem_with_max_in_detail()
    {
        _factory.Service.Reset();
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/products?limit=9999");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("detail").GetString().Should().Contain("100");
    }

    public sealed class CapturingQueryService : IFerretQueryService
    {
        public object? LastQueryRaw { get; private set; }

        public PagedQuery<Product, int>? LastQuery => LastQueryRaw as PagedQuery<Product, int>;

        public void Reset() => LastQueryRaw = null;

        public Task<OffsetResult<T>> SearchOffsetAsync<T, TKey>(
            PagedQuery<T, TKey> query,
            CancellationToken ct = default)
            where T : class
            where TKey : notnull
        {
            LastQueryRaw = query;
            var items = new[] { (object)new Product { Name = "Widget" } };
            var result = new OffsetResult<T>
            {
                Items = items.Cast<T>().ToArray(),
                Limit = 25,
                Page = 2,
                TotalCount = 7,
                HasMore = true,
            };
            return Task.FromResult(result);
        }

        public Task<CursorResult<T>> SearchCursorAsync<T, TKey>(
            PagedQuery<T, TKey> query,
            CancellationToken ct = default)
            where T : class
            where TKey : notnull
            => throw new NotImplementedException();
    }

    public sealed class Product
    {
        public string Name { get; set; } = "";
    }

    public sealed class Factory : IAsyncLifetime
    {
        private WebApplication? _app;

        public CapturingQueryService Service { get; } = new();

        public async Task InitializeAsync()
        {
            _app = MapFerretTestApp.Build(Service);
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
