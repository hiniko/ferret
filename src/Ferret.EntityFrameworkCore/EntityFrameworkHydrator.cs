using System.Data.Common;
using Ferret.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Ferret.EntityFrameworkCore;

public sealed class EntityFrameworkHydrator : IEntityHydrator
{
    private readonly DbContext _context;

    public EntityFrameworkHydrator(DbContext context) => _context = context;

    public async Task<List<T>> HydrateAsync<T>(
        DbConnection connection,
        HydrationRequest request,
        CancellationToken ct) where T : class
    {
#pragma warning disable EF1002    // metadata values, not user input
        var query = _context.Set<T>().FromSqlRaw(request.Sql, request.Parameters.ToArray()).AsNoTracking();
#pragma warning restore EF1002
        return await query.ToListAsync(ct);
    }
}
