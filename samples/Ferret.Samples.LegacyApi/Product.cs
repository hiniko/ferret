using Ferret.Abstractions;

namespace Ferret.Example.LegacyApi;

[SearchableEntity]
public sealed class Product : ISearchableEntity<Guid>
{
    public Guid Id { get; init; }

    [Searchable, Filterable, Sortable]
    public string Name { get; init; } = "";

    [Searchable(Weight = 2.0f), Filterable]
    public string Sku { get; init; } = "";

    [Filterable, Sortable]
    public string Category { get; init; } = "";

    [Filterable, Sortable]
    public decimal Price { get; init; }

    [Filterable, Sortable]
    public int Stock { get; init; }

    [Sortable]
    public DateTime CreatedAt { get; init; }
}
