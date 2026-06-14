namespace Ferret.Abstractions.Search;

/// <summary>
/// Backend-supplied index definition. Used by <c>Ferret.Schema</c> to emit DDL and by
/// <c>Ferret.Migrations</c> to author EF migration operations.
/// </summary>
public sealed record SearchIndexDefinition
{
    public required string IndexName { get; init; }
    public required string TableName { get; init; }
    public required string ColumnName { get; init; }
    public required string IndexSql { get; init; }
    public IReadOnlyList<string> RequiredExtensions { get; init; } = [];
}
