namespace Ferret.Compat.LegacyApi;

/// <summary>
/// Mapping helpers from the engine's <see cref="OffsetResult{T}"/> to the legacy
/// wire-shape <see cref="LegacyPagedResponse{T}"/>.
/// </summary>
public static class LegacyResponseExtensions
{
    /// <summary>
    /// Projects an offset result into the legacy paged-response shape. The
    /// <paramref name="keySelector"/> is kept on the signature so the reshape can later
    /// build per-entity match-info dictionaries once the engine populates them;
    /// in v1 of compat it is unused.
    /// </summary>
    public static LegacyPagedResponse<T> ToLegacyResponse<T, TKey>(
        this OffsetResult<T> result, Func<T, TKey> keySelector)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(keySelector);

        Dictionary<Guid, List<SearchMatchInfo>>? matchInfo = null;
        if (result.MatchInfo is not null)
        {
            matchInfo = ReshapeMatchInfo(result.MatchInfo, result.Items, keySelector);
        }

        return new LegacyPagedResponse<T>
        {
            Items = result.Items,
            Page = result.Page,
            Count = result.Limit,
            Total = result.TotalCount,
            MatchInfo = matchInfo,
        };
    }

    /// <summary>
    /// Stub reshape. The engine does not currently emit per-entity match info, so this
    /// returns an empty dictionary. When the engine starts populating
    /// <see cref="OffsetResult{T}.MatchInfo"/> per item, fill in real reshape logic here.
    /// </summary>
    private static Dictionary<Guid, List<SearchMatchInfo>> ReshapeMatchInfo<T, TKey>(
        SearchMatchInfo source, IReadOnlyList<T> items, Func<T, TKey> keySelector)
        where TKey : notnull
    {
        _ = source;
        _ = items;
        _ = keySelector;
        return new Dictionary<Guid, List<SearchMatchInfo>>();
    }
}
