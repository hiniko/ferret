using Ferret.Abstractions.Querying;
using Ferret.AspNetCore;
using Microsoft.AspNetCore.Mvc;

namespace Ferret.Example.Cursor.Controllers;

[ApiController, Route("products")]
public sealed class ProductsController(
    IFerretQueryService search,
    PaginationDefaultsResolver resolver) : ControllerBase
{
    [HttpGet]
    public Task<CursorResult<Product>> Get(
        [FromQuery] CursorApiQuery q,
        CancellationToken ct)
    {
        var defaults = resolver.Resolve(HttpContext);
        return search.SearchCursorAsync<Product, Guid>(q.ToPagedQuery<Product, Guid>(defaults), ct);
    }
}
