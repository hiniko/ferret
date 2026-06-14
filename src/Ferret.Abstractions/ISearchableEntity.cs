namespace Ferret.Abstractions;

/// <summary>
/// Optional contract for entities Ferret can search, page, filter, and sort.
/// Implementing this interface gives compile-time safety on <c>PagedQuery&lt;T, TKey&gt;</c>.
/// Discovery is attribute-driven via <c>[SearchableEntity]</c> — the interface is not required.
/// </summary>
public interface ISearchableEntity<TKey> where TKey : notnull
{
    /// <summary>Primary key.</summary>
    TKey Id { get; }
}
