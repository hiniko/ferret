using Ferret.Abstractions.Models;
using Ferret.Abstractions.Querying;
using Ferret.Abstractions.Session;
using Ferret.Core.Engine;

namespace Ferret.Core.Querying;

public sealed class FerretCoreQueryService : IFerretQueryService
{
    private readonly IFerretEngine _engine;
    private readonly IFerretSession _session;

    public FerretCoreQueryService(IFerretEngine engine, IFerretSession session)
    {
        _engine = engine;
        _session = session;
    }

    public Task<OffsetResult<T>> SearchOffsetAsync<T, TKey>(
        PagedQuery<T, TKey> query,
        CancellationToken ct = default)
        where T : class
        where TKey : notnull
        => _engine.SearchOffsetAsync(_session, query, ct);

    public Task<CursorResult<T>> SearchCursorAsync<T, TKey>(
        PagedQuery<T, TKey> query,
        CancellationToken ct = default)
        where T : class
        where TKey : notnull
        => _engine.SearchCursorAsync(_session, query, ct);
}
