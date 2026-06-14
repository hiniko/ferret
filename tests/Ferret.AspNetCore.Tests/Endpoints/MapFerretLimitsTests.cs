using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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

public class MapFerretLimitsTests : IClassFixture<MapFerretLimitsTests.Factory>
{
    private readonly Factory _factory;

    public MapFerretLimitsTests(Factory factory) => _factory = factory;

    [Fact]
    public async Task Per_endpoint_default_limit_applies_when_limit_omitted()
    {
        _factory.Service.Reset();
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/products");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.Service.LastQuery.Should().NotBeNull();
        _factory.Service.LastQuery!.Limit.Should().Be(50);
    }

    [Fact]
    public async Task Limit_within_per_endpoint_max_but_over_global_max_is_allowed()
    {
        _factory.Service.Reset();
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/products?limit=150");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.Service.LastQuery!.Limit.Should().Be(150);
    }

    [Fact]
    public async Task Limit_over_per_endpoint_max_returns_400()
    {
        _factory.Service.Reset();
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/products?limit=201");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("detail").GetString().Should().Contain("200");
    }

    public sealed class Factory : IAsyncLifetime
    {
        private WebApplication? _app;

        public MapFerretOffsetTests.CapturingQueryService Service { get; } = new();

        public async Task InitializeAsync()
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddRouting();
            builder.Services.AddFerretAspNetCore();
            builder.Services.AddSingleton<IFerretQueryService>(Service);

            _app = builder.Build();
            _app.MapFerret<MapFerretOffsetTests.Product, int>("/api/products", options =>
            {
                options.DefaultLimit = 50;
                options.MaxLimit = 200;
            });
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
