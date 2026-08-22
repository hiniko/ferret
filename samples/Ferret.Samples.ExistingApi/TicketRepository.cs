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
    /// Search, using the candidate-keys pattern.
    ///
    /// The pre-existing rules here — tenant scoping and the soft-delete global query
    /// filter — cannot be expressed to Ferret as FilterClauses. And projecting an
    /// Include through search duplicates rows when the join fans out.
    ///
    /// So EF decides which rows are eligible; Ferret ranks them.
    /// </summary>
    public async Task<ApiPage<Ticket>> Search(
        Guid organisationId, string term, int page, int perPage, CancellationToken ct)
    {
        // 1. Existing rules, in plain EF. Keys only — no Includes, no duplication.
        //    The global query filter applies here, so soft-deleted rows never appear.
        var candidateIds = await db.Tickets
            .Where(t => t.OrganisationId == organisationId)
            .Select(t => t.Id)
            .ToListAsync(ct);

        if (candidateIds.Count == 0)
            return new ApiPage<Ticket>([], 0, page, perPage);

        // 2. Ferret ranks only those rows.
        var ranked = await search.SearchOffsetAsync<Ticket, Guid>(new PagedQuery<Ticket, Guid>
        {
            Search = term,
            CandidateKeys = candidateIds,
            Limit = perPage,
            Page = page,
            RequestTotalCount = true,
        }, ct);

        // 3. Re-hydrate with the Includes this endpoint needs, then restore rank order.
        //    "WHERE id IN (...)" comes back in database order, not relevance order.
        var ids = ranked.Items.Select(t => t.Id).ToList();
        var hydrated = await db.Tickets
            .Include(t => t.Organisation)
            .Where(t => ids.Contains(t.Id))
            .ToListAsync(ct);

        var byId = hydrated.ToDictionary(t => t.Id);
        var ordered = ids.Where(byId.ContainsKey).Select(id => byId[id]).ToList();

        return new ApiPage<Ticket>(ordered, ranked.TotalCount, page, perPage);
    }
}
