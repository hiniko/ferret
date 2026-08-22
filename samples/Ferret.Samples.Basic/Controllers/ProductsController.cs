using Ferret.Abstractions.Querying;
using Ferret.AspNetCore;
using Microsoft.AspNetCore.Mvc;

namespace Ferret.Example.Basic.Controllers;

[ApiController, Route("products")]
public sealed class ProductsController(
    IFerretQueryService search,
    PaginationDefaultsResolver resolver) : ControllerBase
{
    [HttpGet]
    public Task<OffsetResult<Product>> Get(
        [FromQuery] OffsetApiQuery q,
        CancellationToken ct)
    {
        var defaults = resolver.Resolve(HttpContext);
        return search.SearchOffsetAsync<Product, Guid>(q.ToPagedQuery<Product, Guid>(defaults), ct);
    }
}
