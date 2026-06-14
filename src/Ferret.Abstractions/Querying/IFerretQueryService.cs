using Ferret.Abstractions.Models;

namespace Ferret.Abstractions.Querying;

public interface IFerretQueryService
{
    Task<OffsetResult<T>> SearchOffsetAsync<T, TKey>(
        PagedQuery<T, TKey> query,
        CancellationToken ct = default)
        where T : class
        where TKey : notnull;

    Task<CursorResult<T>> SearchCursorAsync<T, TKey>(
        PagedQuery<T, TKey> query,
        CancellationToken ct = default)
        where T : class
        where TKey : notnull;
}
