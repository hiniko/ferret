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
