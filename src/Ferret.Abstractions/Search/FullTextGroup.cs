using Ferret.Abstractions.Attributes;

namespace Ferret.Abstractions.Search;

/// <summary>
/// A fully resolved fulltext group on a single entity. Built by
/// <c>EntityModelBuilder</c> after merging property attributes, the optional
/// <c>[SearchGroup]</c> entity attribute, and builder-level defaults.
/// </summary>
public sealed record FullTextGroup
{
    public required string Name { get; init; }
    public required string FullTextConfig { get; init; }
    public required ReindexMode Reindex { get; init; }
    public required IReadOnlyList<FullTextGroupProperty> Properties { get; init; }
}

public sealed record FullTextGroupProperty
{
    /// <summary>Property name on the CLR type.</summary>
    public required string PropertyName { get; init; }
    /// <summary>Resolved source column on the entity table.</summary>
    public required string ColumnName { get; init; }
    /// <summary>setweight label, derived from <see cref="SearchablePropertyInfo.Weight"/>.</summary>
    public required FullTextWeightBucket Weight { get; init; }
    /// <summary>Per-property config override; null means the group default applies.</summary>
    public string? FullTextConfigOverride { get; init; }
    /// <summary>Join path to the property's owning entity; null means owner-local.</summary>
    public JoinPath? Join { get; init; }
}

public enum FullTextWeightBucket { A, B, C, D }

public static class FullTextWeightBucketMapper
{
    public static FullTextWeightBucket Bucket(float weight, float a, float b, float c)
    {
        if (weight >= a) return FullTextWeightBucket.A;
        if (weight >= b) return FullTextWeightBucket.B;
        if (weight >= c) return FullTextWeightBucket.C;
        return FullTextWeightBucket.D;
    }
}
