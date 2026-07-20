namespace Ferret.AspNetCore;

/// <summary>Default wire model for <c>MapFerretMultiSearch</c>; replace it via
/// <see cref="MultiSearchEndpointOptions.ShapeResponse"/>.</summary>
public sealed class MultiSearchApiResponse
{
    public required IReadOnlyList<MultiSearchApiGroup> Groups { get; init; }
}

/// <summary>One entity type's hits: projected members only, keyed per the app's JSON
/// property naming policy.</summary>
public sealed class MultiSearchApiGroup
{
    public required string EntityType { get; init; }

    public required IReadOnlyList<IReadOnlyDictionary<string, object?>> Hits { get; init; }
}
