namespace Ferret.Core.Engine;

public sealed record KeyPart
{
    public required string PropertyName { get; init; }
    public required string ColumnName { get; init; }
    public required Type ClrType { get; init; }
}
