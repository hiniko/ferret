using Ferret.Abstractions;
using Ferret.AspNetCore;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ferret.AspNetCore.Tests.Configuration;

public class PaginationDefaultsResolverTests
{
    [Fact]
    public void Library_default_when_nothing_configured()
    {
        var d = new PaginationDefaultsResolver(
            Options.Create(new PaginationOptions { DefaultLimit = 25, MaxLimit = 100 }))
            .Resolve(httpContext: null);
        d.DefaultLimit.Should().Be(25);
        d.MaxLimit.Should().Be(100);
    }

    [Fact]
    public void DI_overrides_library_default()
    {
        var d = new PaginationDefaultsResolver(
            Options.Create(new PaginationOptions { DefaultLimit = 50, MaxLimit = 200 }))
            .Resolve(httpContext: null);
        d.DefaultLimit.Should().Be(50);
        d.MaxLimit.Should().Be(200);
    }

    [Fact]
    public void Attribute_overrides_DI()
    {
        var ctx = new DefaultHttpContext();
        var endpoint = new Endpoint(
            requestDelegate: null,
            metadata: new EndpointMetadataCollection(new PaginationLimitsAttribute { Default = 10, Max = 20 }),
            displayName: "test");
        ctx.SetEndpoint(endpoint);

        var d = new PaginationDefaultsResolver(
            Options.Create(new PaginationOptions { DefaultLimit = 50, MaxLimit = 200 }))
            .Resolve(ctx);
        d.DefaultLimit.Should().Be(10);
        d.MaxLimit.Should().Be(20);
    }
}
