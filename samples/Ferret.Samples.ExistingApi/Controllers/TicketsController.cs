using Ferret.Example.ExistingApi.Domain;
using Microsoft.AspNetCore.Mvc;

namespace Ferret.Example.ExistingApi.Controllers;

/// <summary>
/// A pre-existing controller. Its route, parameters and response shape are unchanged —
/// the only difference is that `?q=` now routes to search instead of returning 400.
/// </summary>
[ApiController, Route("tickets")]
public sealed class TicketsController(TicketRepository repo) : ControllerBase
{
    [HttpGet]
    public async Task<ApiPage<TicketDto>> Get(
        [FromQuery] Guid organisationId,
        [FromQuery] string? q = null,
        [FromQuery] int page = 0,
        [FromQuery] int perPage = 25,
        CancellationToken ct = default)
    {
        var result = string.IsNullOrWhiteSpace(q)
            ? await repo.List(organisationId, page, perPage, ct)
            : await repo.Search(organisationId, q, page, perPage, ct);

        // Project into the API's own envelope. Ferret's OffsetResult never reaches the wire.
        return new ApiPage<TicketDto>(
            result.Data.Select(TicketDto.From).ToList(),
            result.Total,
            result.Page,
            result.PerPage);
    }
}
