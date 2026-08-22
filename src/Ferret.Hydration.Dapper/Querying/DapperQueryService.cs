using System.Data.Common;
using Ferret.Abstractions.Models;
using Ferret.Abstractions.Querying;
using Ferret.Core.Engine;

namespace Ferret.Hydration.Dapper.Querying;

/// <summary>
/// Dapper-backed <see cref="IFerretQueryService"/> — the same front door EF Core users get,
/// so application code injects one interface regardless of the data-access stack underneath.
/// </summary>
/// <remarks>
/// A session owns a connection, so one is opened per call and disposed with the call. Register
/// this with <c>AddFerretDapper(...)</c> rather than constructing it directly.
/// </remarks>
public sealed class DapperQueryService : IFerretQueryService
{
    private readonly Func<CancellationToken, Task<DbConnection>> _connectionFactory;
    private readonly IFerretEngine _engine;
    private readonly ISqlDialect _dialect;

    public DapperQueryService(
        Func<CancellationToken, Task<DbConnection>> connectionFactory,
        IFerretEngine engine,
        ISqlDialect dialect)
    {
        _connectionFactory = connectionFactory;
        _engine = engine;
        _dialect = dialect;
    }

    public async Task<OffsetResult<T>> SearchOffsetAsync<T, TKey>(
        PagedQuery<T, TKey> query,
        CancellationToken ct = default)
        where T : class
        where TKey : notnull
    {
        await using var session = new DapperSession(_connectionFactory, _dialect);
        return await _engine.SearchOffsetAsync<T, TKey>(session, query, ct);
    }

    public async Task<CursorResult<T>> SearchCursorAsync<T, TKey>(
        PagedQuery<T, TKey> query,
        CancellationToken ct = default)
        where T : class
        where TKey : notnull
    {
        await using var session = new DapperSession(_connectionFactory, _dialect);
        return await _engine.SearchCursorAsync<T, TKey>(session, query, ct);
    }

    public async Task<MultiSearchResult> MultiSearchAsync(
        MultiSearchQuery query,
        CancellationToken ct = default)
    {
        await using var session = new DapperSession(_connectionFactory, _dialect);
        return await _engine.MultiSearchAsync(session, query, ct);
    }
}
