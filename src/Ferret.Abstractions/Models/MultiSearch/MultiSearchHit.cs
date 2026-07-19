namespace Ferret.Abstractions.Models;

/// <summary>
/// Base type for multisearch hits. Consumers subclass this per entity with the projected
/// display properties; the engine populates the members declared here.
/// </summary>
public abstract class MultiSearchHit
{
    /// <summary>Entity name the hit belongs to (the entity's table name).</summary>
    public string EntityType { get; set; } = "";

    /// <summary>String-encoded key; composite key parts are joined with U+001F.</summary>
    public string Key { get; set; } = "";

    /// <summary>Backend-native score. Comparable within a group only; trigram scores are distances (lower is better).</summary>
    public double Score { get; set; }
}
