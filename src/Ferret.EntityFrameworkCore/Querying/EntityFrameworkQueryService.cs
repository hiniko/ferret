using Ferret.Abstractions.Querying;
using Microsoft.EntityFrameworkCore;

namespace Ferret.EntityFrameworkCore.Querying;

public sealed class EntityFrameworkQueryService<TContext> : IFerretQueryService
    where TContext : DbContext
{
    private readonly TContext _context;
    private readonly IFerretEngine _engine;
    private readonly ISqlDialect _dialect;

    public EntityFrameworkQueryService(TContext context, IFerretEngine engine, ISqlDialect dialect)
    {
        _context = context;
        _engine = engine;
        _dialect = dialect;
    }

    public async Task<OffsetResult<T>> SearchOffsetAsync<T, TKey>(
        PagedQuery<T, TKey> query,
        CancellationToken ct = default)
        where T : class
        where TKey : notnull
    {
        await using var session = new EntityFrameworkSession(_context, _dialect);
        return await _engine.SearchOffsetAsync<T, TKey>(session, query, ct);
    }

    public async Task<CursorResult<T>> SearchCursorAsync<T, TKey>(
        PagedQuery<T, TKey> query,
        CancellationToken ct = default)
        where T : class
        where TKey : notnull
    {
        await using var session = new EntityFrameworkSession(_context, _dialect);
        return await _engine.SearchCursorAsync<T, TKey>(session, query, ct);
    }
}
