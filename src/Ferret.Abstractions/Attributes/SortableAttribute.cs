namespace Ferret.Abstractions.Attributes;

/// <summary>
/// Allowlists this property for sort clauses received from untrusted input.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class SortableAttribute : Attribute { }
