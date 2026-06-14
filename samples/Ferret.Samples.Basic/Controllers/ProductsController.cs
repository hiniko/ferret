using Ferret.Abstractions;
using Ferret.AspNetCore;
using Ferret.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;

namespace Ferret.Example.Basic.Controllers;

[ApiController, Route("products")]
public sealed class ProductsController(
    AppDbContext db,
    PaginationDefaultsResolver resolver,
    IServiceProvider sp) : ControllerBase
{
    [HttpGet]
    public Task<OffsetResult<Product>> Get(
        [FromQuery] OffsetApiQuery q,
        CancellationToken ct)
    {
        var defaults = resolver.Resolve(HttpContext);
        return db.SearchOffsetAsync<Product, Guid>(sp, q.ToPagedQuery<Product, Guid>(defaults), ct);
    }
}
