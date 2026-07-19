namespace Ferret.Abstractions.Models;

/// <summary>Grouped multisearch result; groups appear in entity registration order.</summary>
public sealed class MultiSearchResult
{
    public required IReadOnlyList<MultiSearchGroup> Groups { get; init; }
}
