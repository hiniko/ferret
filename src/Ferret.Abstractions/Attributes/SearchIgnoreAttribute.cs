namespace Ferret.Abstractions.Attributes;

/// <summary>
/// Excludes specific properties on the marked entity from Ferret discovery
/// (search indexing, filterable/sortable allowlists).
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class SearchIgnoreAttribute : Attribute
{
    public IReadOnlyList<string> PropertyNames { get; }

    public SearchIgnoreAttribute(params string[] propertyNames)
    {
        PropertyNames = propertyNames ?? [];
    }
}
