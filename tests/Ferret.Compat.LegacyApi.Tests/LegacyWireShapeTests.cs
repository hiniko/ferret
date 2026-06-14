using System.Text.Json;
using Ferret.Abstractions;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Ferret.Compat.LegacyApi.Tests;

public class LegacyWireShapeTests : IAsyncLifetime
{
    private IHost _host = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _host = await Host.CreateDefaultBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .UseContentRoot(AppContext.BaseDirectory)
                .ConfigureServices(s => s.AddControllers().AddApplicationPart(typeof(EchoLegacyController).Assembly))
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(e => e.MapControllers());
                }))
            .StartAsync();
        _client = _host.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    [Fact]
    public async Task GET_with_legacy_query_params_binds_and_serialises_legacy_shape()
    {
        var resp = await _client.GetAsync(
            "/products?page=2&page_size=5&search=widget&filter=name:eq:Blue&filter=price:gt:10&sort=name:desc&include_match_info=true");
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        root.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["items", "page", "count", "total", "match_info"]);
        root.GetProperty("page").GetInt32().Should().Be(2);
        root.GetProperty("count").GetInt32().Should().Be(5);
        root.GetProperty("total").GetInt32().Should().Be(42);
        root.GetProperty("match_info").ValueKind.Should().Be(JsonValueKind.Null);

        var echoed = JsonSerializer.Deserialize<EchoBoundQuery>(
            root.GetProperty("items").EnumerateArray().First().GetRawText(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        echoed.Page.Should().Be(2);
        echoed.PageSize.Should().Be(5);
        echoed.Search.Should().Be("widget");
        echoed.IncludeMatchInfo.Should().BeTrue();
        echoed.Filter.Should().HaveCount(2);
        echoed.Filter[0].Should().Be("name:eq:Blue");
        echoed.Filter[1].Should().Be("price:gt:10");
        echoed.Sort.Should().ContainSingle().Which.Should().Be("name:desc");
    }

    [Fact]
    public async Task Default_pagination_applied_when_params_absent()
    {
        var resp = await _client.GetAsync("/products");
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var item = JsonSerializer.Deserialize<EchoBoundQuery>(
            doc.RootElement.GetProperty("items").EnumerateArray().First().GetRawText(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        item.Page.Should().BeNull();
        item.PageSize.Should().BeNull();
    }

    public sealed record EchoBoundQuery
    {
        public int? Page { get; init; }
        public int? PageSize { get; init; }
        public string? Search { get; init; }
        public bool IncludeMatchInfo { get; init; }
        public List<string> Sort { get; init; } = [];
        public List<string> Filter { get; init; } = [];
    }
}

[ApiController, Route("products")]
public sealed class EchoLegacyController : ControllerBase
{
    [HttpGet]
    public LegacyPagedResponse<LegacyWireShapeTests.EchoBoundQuery> Get([FromQuery] LegacyApiQuery q) =>
        new()
        {
            Items =
            [
                new LegacyWireShapeTests.EchoBoundQuery
                {
                    Page = q.Page,
                    PageSize = q.PageSize,
                    Search = q.Search,
                    IncludeMatchInfo = q.IncludeMatchInfo,
                    Sort = q.Sort.Select(s => $"{s.Field}:{(s.Direction == SortDirection.Descending ? "desc" : "asc")}").ToList(),
                    Filter = q.Filter.Select(f => $"{f.Field}:{OpSlug(f.Operator)}:{f.Value}").ToList(),
                }
            ],
            Page = q.Page ?? 0,
            Count = q.PageSize ?? 0,
            Total = 42,
        };

    private static string OpSlug(FilterOperator op) => op switch
    {
        FilterOperator.Equals => "eq",
        FilterOperator.NotEquals => "neq",
        FilterOperator.Contains => "contains",
        FilterOperator.GreaterThan => "gt",
        FilterOperator.GreaterThanOrEqual => "gte",
        FilterOperator.LessThan => "lt",
        FilterOperator.LessThanOrEqual => "lte",
        FilterOperator.In => "in",
        _ => op.ToString(),
    };
}
