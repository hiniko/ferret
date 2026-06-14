namespace Ferret.Abstractions.Configuration;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class PaginationLimitsAttribute : Attribute
{
    public int Default { get; init; } = 25;
    public int Max { get; init; } = 100;
}
