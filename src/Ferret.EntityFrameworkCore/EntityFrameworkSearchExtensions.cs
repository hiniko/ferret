using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ferret.EntityFrameworkCore;

public static class EntityFrameworkSearchExtensions
{
    /// <summary>Convenience wrapper: opens an EF session, runs the engine in offset mode, returns the page.</summary>
    public static async Task<OffsetResult<T>> SearchOffsetAsync<T, TKey>(
        this DbContext context,
        IServiceProvider sp,
        PagedQuery<T, TKey> query,
        CancellationToken ct = default)
        where T : class
        where TKey : notnull
    {
        var engine = sp.GetRequiredService<IFerretEngine>();
        var dialect = sp.GetRequiredService<ISqlDialect>();
        await using var session = new EntityFrameworkSession(context, dialect);
        return await engine.SearchOffsetAsync<T, TKey>(session, query, ct);
    }

    /// <summary>Convenience wrapper: opens an EF session, runs the engine in cursor mode, returns the page.</summary>
    public static async Task<CursorResult<T>> SearchCursorAsync<T, TKey>(
        this DbContext context,
        IServiceProvider sp,
        PagedQuery<T, TKey> query,
        CancellationToken ct = default)
        where T : class
        where TKey : notnull
    {
        var engine = sp.GetRequiredService<IFerretEngine>();
        var dialect = sp.GetRequiredService<ISqlDialect>();
        await using var session = new EntityFrameworkSession(context, dialect);
        return await engine.SearchCursorAsync<T, TKey>(session, query, ct);
    }
}
