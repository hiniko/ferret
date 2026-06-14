using Ferret.Abstractions.Querying;
using Ferret.AspNetCore;
using Ferret.AspNetCore.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Ferret.AspNetCore.Tests.Endpoints;

public class MapFerretTestApp
{
    public static WebApplication Build(IFerretQueryService service)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();
        builder.Services.AddFerretAspNetCore();
        builder.Services.AddSingleton(service);

        var app = builder.Build();
        app.MapFerret<MapFerretOffsetTests.Product, int>("/api/products");
        return app;
    }
}
