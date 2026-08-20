using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ferret.AspNetCore.Tests.Binding;

/// <summary>
/// Binds through a real MVC pipeline, not a hand-built ModelBindingContext. The
/// clause binders were only ever unit-tested in isolation, so a total failure of
/// [FromQuery] complex binding on these records went unnoticed.
/// </summary>
public sealed class MvcControllerBindingTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers()
            .PartManager.ApplicationParts.Add(
                new AssemblyPart(typeof(MvcControllerBindingTests).Assembly));
        builder.Services.AddFerretAspNetCore();
        builder.Services.AddFerretMvcBinding();

        _app = builder.Build();
        _app.MapControllers();
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    private async Task<Echo> Get(string qs)
    {
        var res = await _client.GetAsync("/bind" + qs);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<Echo>())!;
    }

    [Fact]
    public async Task Binds_search_term()
    {
        (await Get("?q=lamp")).Q.Should().Be("lamp");
    }

    [Fact]
    public async Task Binds_limit()
    {
        (await Get("?limit=2")).Limit.Should().Be(2);
    }

    [Fact]
    public async Task Binds_search_term_and_limit_together()
    {
        var e = await Get("?q=lamp&limit=2");
        e.Q.Should().Be("lamp");
        e.Limit.Should().Be(2);
    }

    [Fact]
    public async Task Binds_sort_clauses()
    {
        (await Get("?sort=Name:desc")).SortCount.Should().Be(1);
    }

    [Fact]
    public async Task Binds_filter_clauses()
    {
        (await Get("?filter=price:gt:10")).FilterCount.Should().Be(1);
    }

    [Fact]
    public async Task Sort_does_not_suppress_scalar_binding()
    {
        // Regression: with a clause binder in play, scalar properties silently stopped
        // binding — limit fell back to the configured default and q went null.
        var e = await Get("?q=lamp&limit=2&sort=Name:desc&filter=price:gt:10");
        e.Q.Should().Be("lamp");
        e.Limit.Should().Be(2);
        e.SortCount.Should().Be(1);
        e.FilterCount.Should().Be(1);
    }

    [Fact]
    public async Task Binds_page_and_count()
    {
        var e = await Get("?page=3&count=false");
        e.Page.Should().Be(3);
        e.Count.Should().BeFalse();
    }

    [Fact]
    public async Task Count_defaults_to_true_when_absent()
    {
        (await Get("?q=x")).Count.Should().BeTrue();
    }

    [Fact]
    public async Task Binds_fields_list()
    {
        (await Get("?fields=Name&fields=Sku")).FieldCount.Should().Be(2);
    }

    [Fact]
    public async Task Binds_when_action_parameter_is_named_q()
    {
        // MVC prefixes complex binding with the action parameter name. An action that
        // names its parameter `q` (as the docs did) collides with the ?q= search key:
        // MVC then looks for `q.Q`, `q.limit`, ... and every property silently defaults.
        var res = await _client.GetAsync("/bind-collide?q=lamp&limit=2");
        res.EnsureSuccessStatusCode();
        var e = (await res.Content.ReadFromJsonAsync<Echo>())!;
        e.Q.Should().Be("lamp");
        e.Limit.Should().Be(2);
    }

    [Fact]
    public async Task Cursor_query_binds_scalars_and_clauses()
    {
        var res = await _client.GetAsync("/bind-cursor?q=lamp&limit=7&sort=Name:desc");
        res.EnsureSuccessStatusCode();
        var e = (await res.Content.ReadFromJsonAsync<Echo>())!;
        e.Q.Should().Be("lamp");
        e.Limit.Should().Be(7);
        e.SortCount.Should().Be(1);
    }

    public sealed record Echo(
        string? Q, int? Limit, int SortCount, int FilterCount,
        int FieldCount, int? Page, bool Count);
}

[ApiController]
public sealed class BindingProbeController : ControllerBase
{
    [HttpGet("/bind")]
    public MvcControllerBindingTests.Echo Offset([FromQuery] OffsetApiQuery query) =>
        new(query.Q, query.Limit, query.Sort.Count, query.Filter.Count, query.Fields.Count, query.Page, query.Count);

    [HttpGet("/bind-collide")]
    public MvcControllerBindingTests.Echo Collide([FromQuery] OffsetApiQuery q) =>
        new(q.Q, q.Limit, q.Sort.Count, q.Filter.Count, q.Fields.Count, q.Page, q.Count);

    [HttpGet("/bind-cursor")]
    public MvcControllerBindingTests.Echo Cursor([FromQuery] CursorApiQuery query) =>
        new(query.Q, query.Limit, query.Sort.Count, query.Filter.Count, query.Fields.Count, null, true);
}
