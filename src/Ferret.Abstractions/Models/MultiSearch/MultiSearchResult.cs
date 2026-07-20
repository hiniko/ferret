namespace Ferret.Abstractions.Models;

/// <summary>Grouped multisearch result; groups appear in entity registration order.</summary>
public sealed class MultiSearchResult
{
    public required IReadOnlyList<MultiSearchGroup> Groups { get; init; }

    /// <summary>Hits for the entity type <typeparamref name="T"/>, best-match first.</summary>
    public IReadOnlyList<T> HitsOf<T>()
        where T : class
    {
        return Groups.SelectMany(g => g.Hits).OfType<T>().ToList();
    }
}
