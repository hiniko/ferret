using Ferret.Abstractions.Models;

namespace Ferret.Abstractions.Attributes;

/// <summary>
/// Allowlists this property for filter clauses received from untrusted input
/// (<c>PagedQuery.Filter</c>, HTTP model binders).
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class FilterableAttribute : Attribute
{
    /// <summary>Restrict to a subset of operators. Empty = all supported operators allowed.</summary>
    public FilterOperator[] Operators { get; init; } = [];
}
