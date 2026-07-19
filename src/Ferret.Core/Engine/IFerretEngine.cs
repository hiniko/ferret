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

    Task<MultiSearchResult> MultiSearchAsync(
        IFerretSession session,
        MultiSearchQuery query,
        CancellationToken ct = default);
}

/// <summary>Builder-flavored sugar over <see cref="IFerretEngine.MultiSearchAsync"/>.</summary>
public static class FerretEngineMultiSearchExtensions
{
    public static Task<MultiSearchResult> MultiSearchAsync(
        this IFerretEngine engine,
        IFerretSession session,
        Action<MultiSearchQueryBuilder> configure,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new MultiSearchQueryBuilder();
        configure(builder);
        return engine.MultiSearchAsync(session, builder.Build(), ct);
    }
}
