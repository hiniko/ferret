using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ferret.Abstractions.Models;
using Ferret.Abstractions.Querying;
using Ferret.AspNetCore;
using Ferret.AspNetCore.DependencyInjection;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ferret.AspNetCore.Tests.Endpoints;

public class MapFerretCursorTests : IClassFixture<MapFerretCursorTests.Factory>
{
    private readonly Factory _factory;

    public MapFerretCursorTests(Factory factory) => _factory = factory;

    [Fact]
    public async Task Cursor_happy_path_returns_camelCase_json()
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
        root.GetProperty("nextCursor").GetString().Should().Be("next-token");
        root.GetProperty("hasMore").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Cursor_after_maps_forward()
    {
        _factory.Service.Reset();
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/products?after=tok");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var captured = _factory.Service.LastQuery;
        captured.Should().NotBeNull();
        captured!.Mode.Should().Be(PaginationMode.Cursor);
        captured.CursorDirection.Should().Be(CursorDirection.Forward);
        captured.Cursor.Should().Be("tok");
    }

    [Fact]
    public async Task Cursor_before_maps_backward()
    {
        _factory.Service.Reset();
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/products?before=tok");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var captured = _factory.Service.LastQuery;
        captured.Should().NotBeNull();
        captured!.CursorDirection.Should().Be(CursorDirection.Backward);
        captured.Cursor.Should().Be("tok");
    }

    [Fact]
    public async Task Cursor_both_after_and_before_returns_400()
    {
        _factory.Service.Reset();
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/products?after=a&before=b");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Cursor_invalid_cursor_returns_400_problem()
    {
        _factory.Service.Reset();
        _factory.Service.ThrowInvalidCursor = true;
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/products?after=bad");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("detail").GetString().Should().Contain("cursor");
    }

    public sealed class CapturingCursorService : IFerretQueryService
    {
        public object? LastQueryRaw { get; private set; }

        public bool ThrowInvalidCursor { get; set; }

        public PagedQuery<Product, int>? LastQuery => LastQueryRaw as PagedQuery<Product, int>;

        public void Reset()
        {
            LastQueryRaw = null;
            ThrowInvalidCursor = false;
        }

        public Task<OffsetResult<T>> SearchOffsetAsync<T, TKey>(
            PagedQuery<T, TKey> query,
            CancellationToken ct = default)
            where T : class
            where TKey : notnull
            => throw new NotImplementedException();

        public Task<CursorResult<T>> SearchCursorAsync<T, TKey>(
            PagedQuery<T, TKey> query,
            CancellationToken ct = default)
            where T : class
            where TKey : notnull
        {
            LastQueryRaw = query;
            if (ThrowInvalidCursor)
                throw new InvalidCursorException("cursor invalid for current sort/filter; restart without cursor");

            var items = new[] { (object)new Product { Name = "Widget" } };
            var result = new CursorResult<T>
            {
                Items = items.Cast<T>().ToArray(),
                Limit = 25,
                NextCursor = "next-token",
                HasMore = true,
            };
            return Task.FromResult(result);
        }
    }

    public sealed class Product
    {
        public string Name { get; set; } = "";
    }

    public sealed class Factory : IAsyncLifetime
    {
        private WebApplication? _app;

        public CapturingCursorService Service { get; } = new();

        public async Task InitializeAsync()
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddRouting();
            builder.Services.AddFerretAspNetCore();
            builder.Services.AddSingleton<IFerretQueryService>(Service);

            _app = builder.Build();
            _app.MapFerret<Product, int>("/api/products", options =>
                options.Pagination = FerretEndpointPaginationMode.Cursor);

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
