using System.Net;
using System.Text.Json;
using Ferret.Abstractions.Models;
using Ferret.Abstractions.Querying;
using Ferret.AspNetCore;
using Ferret.AspNetCore.DependencyInjection;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ferret.AspNetCore.Tests.Endpoints;

public class MapFerretOpenApiTests
{
    [Fact]
    public async Task Offset_endpoint_is_documented_with_tag_response_and_query_parameters()
    {
        await using var app = BuildApp(builder => builder.MapFerret<Product, int>("/api/products"));

        var client = app.GetTestClient();
        var response = await client.GetAsync("/openapi/v1.json");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var operation = root
            .GetProperty("paths")
            .GetProperty("/api/products")
            .GetProperty("get");

        operation.GetProperty("tags").EnumerateArray()
            .Select(t => t.GetString())
            .Should().Contain("Ferret");

        var ok = operation.GetProperty("responses").GetProperty("200");
        ok.GetProperty("content").GetProperty("application/json")
            .GetProperty("schema").TryGetProperty("$ref", out _).Should().BeTrue();

        operation.GetProperty("responses").TryGetProperty("400", out _).Should().BeTrue();

        var paramNames = operation.GetProperty("parameters").EnumerateArray()
            .Select(p => p.GetProperty("name").GetString())
            .ToArray();

        paramNames.Should().Contain(new[] { "q", "fields", "match_info", "filter", "sort", "limit" });
        paramNames.Should().Contain("page");
        paramNames.Should().Contain("count");
    }

    [Fact]
    public async Task Cursor_endpoint_documents_cursor_specific_parameters()
    {
        await using var app = BuildApp(builder => builder.MapFerret<Product, int>(
            "/api/products",
            options => options.Pagination = FerretEndpointPaginationMode.Cursor));

        var client = app.GetTestClient();
        var response = await client.GetAsync("/openapi/v1.json");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        var operation = doc.RootElement
            .GetProperty("paths")
            .GetProperty("/api/products")
            .GetProperty("get");

        var paramNames = operation.GetProperty("parameters").EnumerateArray()
            .Select(p => p.GetProperty("name").GetString())
            .ToArray();

        paramNames.Should().Contain(new[] { "after", "before" });
    }

    [Fact]
    public async Task WithTags_chaining_overrides_tag_in_document()
    {
        await using var app = BuildApp(builder =>
            builder.MapFerret<Product, int>("/api/products").WithTags("Catalog"));

        var client = app.GetTestClient();
        var response = await client.GetAsync("/openapi/v1.json");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        var operation = doc.RootElement
            .GetProperty("paths")
            .GetProperty("/api/products")
            .GetProperty("get");

        operation.GetProperty("tags").EnumerateArray()
            .Select(t => t.GetString())
            .Should().Contain("Catalog");
    }

    private static WebApplication BuildApp(Action<WebApplication> map)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();
        builder.Services.AddFerretAspNetCore();
        builder.Services.AddOpenApi();
        builder.Services.AddSingleton<IFerretQueryService>(new StubQueryService());

        var openApiServices = builder.Services
            .Where(s => s.ServiceType.FullName!.Contains("OpenApi"))
            .Select(s => $"{(s.IsKeyedService ? "keyed:" + s.ServiceKey : "plain")} {s.ServiceType.FullName}")
            .ToArray();
        System.IO.File.WriteAllLines("/tmp/openapi_services.txt", openApiServices);

        var app = builder.Build();
        app.MapOpenApi();
        map(app);
        app.StartAsync().GetAwaiter().GetResult();
        return app;
    }

    private sealed class StubQueryService : IFerretQueryService
    {
        public Task<OffsetResult<T>> SearchOffsetAsync<T, TKey>(
            PagedQuery<T, TKey> query,
            CancellationToken ct = default)
            where T : class
            where TKey : notnull
            => Task.FromResult(new OffsetResult<T> { Items = Array.Empty<T>() });

        public Task<CursorResult<T>> SearchCursorAsync<T, TKey>(
            PagedQuery<T, TKey> query,
            CancellationToken ct = default)
            where T : class
            where TKey : notnull
            => Task.FromResult(new CursorResult<T> { Items = Array.Empty<T>() });
    }

    public sealed class Product
    {
        public string Name { get; set; } = "";
    }
}
