namespace Ferret.Abstractions.Models;

/// <summary>One entity type's ranked hits within a multisearch result.</summary>
public sealed class MultiSearchGroup
{
    public required string EntityType { get; init; }

    /// <summary>Hits ordered best-match first. Runtime type is the searched entity type, populated
    /// with the projected members only — treat as display data, never persist one.</summary>
    public required IReadOnlyList<object> Hits { get; init; }
}
