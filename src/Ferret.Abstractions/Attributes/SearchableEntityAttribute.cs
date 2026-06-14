namespace Ferret.Abstractions.Attributes;

/// <summary>
/// Marks a CLR class as a Ferret-discoverable entity. Optional in EF mode (defaults read from
/// <see cref="T:Microsoft.EntityFrameworkCore.Metadata.IEntityType"/>) and optional in standalone
/// mode when the configured naming strategy can resolve table and key.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SearchableEntityAttribute : Attribute
{
    /// <summary>Override the resolved table name.</summary>
    public string? Table { get; init; }

    /// <summary>Override the resolved schema.</summary>
    public string? Schema { get; init; }

    private readonly string _keyProperty = "Id";

    /// <summary>CLR property name for the primary key. Defaults to <c>"Id"</c>.</summary>
    public string KeyProperty
    {
        get => _keyProperty;
        init { _keyProperty = value; KeyPropertyExplicitlySet = true; }
    }

    /// <summary>True when <see cref="KeyProperty"/> was assigned in the attribute usage (even to "Id").</summary>
    public bool KeyPropertyExplicitlySet { get; private init; }

    /// <summary>CLR property names forming a composite primary key, in order. Mutually exclusive with <see cref="KeyProperty"/>.</summary>
    public string[]? KeyProperties { get; init; }
}
