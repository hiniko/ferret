namespace Ferret.Core.Engine;

public interface IFerretEngine
{
    Task<OffsetResult<T>> SearchOffsetAsync<T, TKey>(
        IFerretSession session,
        PagedQuery<T, TKey> query,
        CancellationToken ct = default)
        where T : class
        where TKey : notnull;

    Task<CursorResult<T>> SearchCursorAsync<T, TKey>(
        IFerretSession session,
        PagedQuery<T, TKey> query,
        CancellationToken ct = default)
        where T : class
        where TKey : notnull;

    Task ReindexAsync<T>(
        IFerretSession session,
        string group,
        ReindexOptions? options = null,
        CancellationToken ct = default)
        where T : class;
}
