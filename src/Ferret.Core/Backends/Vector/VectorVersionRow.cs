namespace Ferret.Core.Backends.Vector;

public sealed record VectorVersionRow
{
    public required long VersionId { get; init; }
    public required string Entity { get; init; }
    public required string GroupName { get; init; }
    public required string Model { get; init; }
    public required int Dimensions { get; init; }
    public required string ColumnName { get; init; }
    public required string Status { get; init; }
}
