using Ferret.Abstractions;
using Ferret.AspNetCore;
using Ferret.Compat.LegacyApi;
using Ferret.EntityFrameworkCore;
using Ferret.Example.LegacyApi;
using Microsoft.AspNetCore.Mvc;

namespace Ferret.Example.LegacyApi.Controllers;

[ApiController, Route("products")]
public sealed class ProductsController(
    AppDbContext db,
    PaginationDefaultsResolver resolver,
    IServiceProvider sp) : ControllerBase
{
    [HttpGet]
    public async Task<LegacyPagedResponse<Product>> Get(
        [FromQuery] LegacyApiQuery q,
        CancellationToken ct)
    {
        var defaults = resolver.Resolve(HttpContext);
        var result = await db.SearchOffsetAsync<Product, Guid>(sp,
            q.ToPagedQuery<Product, Guid>(defaults), ct);
        return result.ToLegacyResponse(p => p.Id);
    }
}
