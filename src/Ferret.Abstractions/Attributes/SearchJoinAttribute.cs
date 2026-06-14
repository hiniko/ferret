namespace Ferret.Abstractions.Attributes;

/// <summary>
/// Marks a collection navigation as a search join — children's <see cref="SearchableAttribute"/>
/// properties contribute to the parent's search ranking. Aggregate hop budget across a join chain
/// is capped at 5 for v1; engine throws at registration if the chain exceeds the budget.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class SearchJoinAttribute : Attribute
{
    /// <summary>Hops contributed by this navigation. Default 1.</summary>
    public int Depth { get; init; } = 1;

    /// <summary>Override the foreign-key column name (standalone mode only).</summary>
    public string? ForeignKey { get; init; }
}
