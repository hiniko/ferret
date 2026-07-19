namespace Ferret.Abstractions.Models;

/// <summary>One entity type's ranked hits within a multisearch result.</summary>
public sealed class MultiSearchGroup
{
    public required string EntityType { get; init; }

    /// <summary>Hits ordered best-match first; runtime type is the concrete <see cref="MultiSearchHit"/> subclass registered for the entity.</summary>
    public required IReadOnlyList<MultiSearchHit> Hits { get; init; }
}
