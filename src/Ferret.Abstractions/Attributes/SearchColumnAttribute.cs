namespace Ferret.Abstractions.Attributes;

/// <summary>
/// Overrides the column name resolved by the active naming strategy.
/// In EF mode the EF column metadata wins; this attribute applies in standalone mode.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class SearchColumnAttribute : Attribute
{
    public required string Name { get; init; }
}
