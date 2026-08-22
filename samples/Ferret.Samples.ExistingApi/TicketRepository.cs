using Ferret.Example.ExistingApi.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ferret.Example.ExistingApi;

/// <summary>
/// A pre-existing repository. Search was added by injecting IFerretQueryService
/// alongside the DbContext this class already had.
/// </summary>
public sealed class TicketRepository(AppDbContext db, IFerretQueryService search)
{
    /// <summary>
    /// Plain listing — the code path that existed before Ferret. Untouched.
    /// </summary>
    public async Task<ApiPage<Ticket>> List(Guid organisationId, int page, int perPage, CancellationToken ct)
    {
        var query = db.Tickets
            .Include(t => t.Organisation)
            .Where(t => t.OrganisationId == organisationId);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip(page * perPage).Take(perPage)
            .ToListAsync(ct);

        return new ApiPage<Ticket>(items, total, page, perPage);
    }

    /// <summary>
    /// Search with server-enforced scoping.
    ///
    /// Tenant scoping and soft delete are expressed as authoritative filters, so they are
    /// compiled into the search query and applied by Postgres. Nothing is pre-loaded: no list
    /// of candidate ids crosses the wire, and the work stays in the database where the indexes
    /// are.
    ///
    /// "Authoritative" means a caller-supplied filter on the same field is REPLACED, not
    /// combined — a request cannot widen the tenant boundary by filtering on it itself.
    /// </summary>
    public async Task<ApiPage<Ticket>> Search(
        Guid organisationId, string term, int page, int perPage, CancellationToken ct)
    {
        var ranked = await search.SearchOffsetAsync<Ticket, Guid>(new PagedQuery<Ticket, Guid>
        {
            Search = term,
            Limit = perPage,
            Page = page,
            RequestTotalCount = true,
            AuthoritativeFilter =
            [
                Filters.Eq(nameof(Ticket.OrganisationId), organisationId),
                Filters.Eq(nameof(Ticket.IsDeleted), false),
            ],
        }, ct);

        // Ferret returns ranked entities. Re-hydrate with the Includes this endpoint needs,
        // then restore rank order — "WHERE id IN (...)" comes back in database order.
        var ids = ranked.Items.Select(t => t.Id).ToList();
        if (ids.Count == 0)
            return new ApiPage<Ticket>([], ranked.TotalCount, page, perPage);

        var hydrated = await db.Tickets
            .Include(t => t.Organisation)
            .Where(t => ids.Contains(t.Id))
            .ToListAsync(ct);

        var byId = hydrated.ToDictionary(t => t.Id);
        var ordered = ids.Where(byId.ContainsKey).Select(id => byId[id]).ToList();

        return new ApiPage<Ticket>(ordered, ranked.TotalCount, page, perPage);
    }
}
