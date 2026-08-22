using Ferret.Abstractions.Models;

namespace Ferret.Abstractions.Querying;

/// <summary>
/// Entry point for running a <see cref="PagedQuery{T,TKey}"/> or <see cref="MultiSearchQuery"/>
/// against a configured backend (EF Core or Dapper) and getting back hydrated, paged results.
/// </summary>
public interface IFerretQueryService
{
    /// <summary>Runs <paramref name="query"/> in offset-paging mode and returns a page with a total count.</summary>
    Task<OffsetResult<T>> SearchOffsetAsync<T, TKey>(
        PagedQuery<T, TKey> query,
        CancellationToken ct = default)
        where T : class
        where TKey : notnull;

    /// <summary>Runs <paramref name="query"/> in cursor-paging mode and returns a page with forward/backward tokens.</summary>
    Task<CursorResult<T>> SearchCursorAsync<T, TKey>(
        PagedQuery<T, TKey> query,
        CancellationToken ct = default)
        where T : class
        where TKey : notnull;

    /// <summary>
    /// Runs <paramref name="query"/> across multiple entity types in one round trip and returns
    /// interleaved, projected hits ranked together.
    /// </summary>
    Task<MultiSearchResult> MultiSearchAsync(
        MultiSearchQuery query,
        CancellationToken ct = default);
}

/// <summary>Builder-flavored sugar over <see cref="IFerretQueryService.MultiSearchAsync"/>.</summary>
public static class FerretQueryServiceMultiSearchExtensions
{
    public static Task<MultiSearchResult> MultiSearchAsync(
        this IFerretQueryService service,
        Action<MultiSearchQueryBuilder> configure,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new MultiSearchQueryBuilder();
        configure(builder);
        return service.MultiSearchAsync(builder.Build(), ct);
    }
}
