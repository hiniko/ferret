using System.Text.Json.Serialization;

namespace Ferret.Compat.LegacyApi;

/// <summary>
/// Legacy-API wire-shape paged response. Property names match the original backend's
/// <c>PagedQueryViewModel</c> JSON (<c>items</c>, <c>page</c>, <c>count</c>, <c>total</c>,
/// <c>match_info</c>) so existing clients can deserialize responses unchanged.
/// </summary>
public sealed record LegacyPagedResponse<T>
{
    [JsonPropertyName("items")]
    public required IReadOnlyList<T> Items { get; init; }

    [JsonPropertyName("page")]
    public int Page { get; init; }

    [JsonPropertyName("count")]
    public int Count { get; init; }

    [JsonPropertyName("total")]
    public int Total { get; init; }

    /// <summary>
    /// Per-entity match metadata. Always serialised (legacy clients expect the key).
    /// Note: Ferret v1's engine does not yet populate <see cref="OffsetResult{T}.MatchInfo"/>,
    /// so this is currently always <c>null</c> on the wire even when
    /// <c>include_match_info=true</c> is requested.
    /// </summary>
    [JsonPropertyName("match_info")]
    public Dictionary<Guid, List<SearchMatchInfo>>? MatchInfo { get; init; }
}
