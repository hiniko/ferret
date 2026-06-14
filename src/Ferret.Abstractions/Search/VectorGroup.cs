using Ferret.Abstractions.Attributes;

namespace Ferret.Abstractions.Search;

/// <summary>
/// A fully resolved vector group on a single entity. Built by EntityModelBuilder after merging
/// property attributes. Vector ranks by distance, so there is no weight bucket or text config.
/// </summary>
public sealed record VectorGroup
{
    public required string Name { get; init; }
    public required int Dimensions { get; init; }
    public ReindexMode Reindex { get; init; } = ReindexMode.Concurrent;
    public required IReadOnlyList<VectorGroupProperty> Properties { get; init; }
}

public sealed record VectorGroupProperty
{
    public required string PropertyName { get; init; }
    public required string ColumnName { get; init; }
    /// <summary>Sibling property name holding the source text for write-side embedding.</summary>
    public required string EmbeddingSource { get; init; }
    /// <summary>Join path to the property's owning entity; null means owner-local.</summary>
    public JoinPath? Join { get; init; }
}
