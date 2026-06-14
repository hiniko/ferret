namespace Ferret.Abstractions.Attributes;

/// <summary>
/// Group-level configuration for fulltext groups declared on the entity via
/// <see cref="SearchableAttribute.Group"/>. Optional — used only when a group
/// needs a config that differs from the builder default.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class SearchGroupAttribute : Attribute
{
    public string Name { get; }
    public string? FullTextConfig { get; init; }
    public ReindexMode? Reindex { get; init; }

    public SearchGroupAttribute(string name)
    {
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Group name must be non-empty.", nameof(name))
            : name;
    }
}
