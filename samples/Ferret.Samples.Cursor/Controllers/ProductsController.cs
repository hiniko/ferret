using Ferret.AspNetCore;
using Ferret.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;

namespace Ferret.Example.Cursor.Controllers;

[ApiController, Route("products")]
public sealed class ProductsController(
    AppDbContext db,
    PaginationDefaultsResolver resolver,
    IServiceProvider sp) : ControllerBase
{
    [HttpGet]
    public Task<CursorResult<Product>> Get(
        [FromQuery] CursorApiQuery q,
        CancellationToken ct)
    {
        var defaults = resolver.Resolve(HttpContext);
        return db.SearchCursorAsync(sp, q.ToPagedQuery<Product, Guid>(defaults), ct);
    }
}
