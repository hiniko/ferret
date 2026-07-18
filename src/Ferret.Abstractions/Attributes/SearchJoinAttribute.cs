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

    /// <summary>
    /// Raw SQL condition applied to the joined table in query-time search joins, e.g. to
    /// exclude soft-deleted or hidden children from parent ranking. Use the <c>{c}</c>
    /// placeholder for the joined table's alias:
    /// <c>Where = "{c}.deleted_at IS NULL AND {c}.hidden = false"</c>.
    /// Trusted developer metadata — never interpolate user input. Applied by query-time
    /// join backends (trigram); index-time backends (full-text sidecar) do not apply it.
    /// </summary>
    public string? Where { get; init; }
}
