using Ferret.Abstractions.Search;

namespace Ferret.Migrations.Annotations;

internal sealed record VectorEntityGroupsDto
{
    public required string SidecarTable { get; init; }
    public string? SidecarSchema { get; init; }
    public required string SourceTable { get; init; }
    public string? SourceSchema { get; init; }
    public required string IdColumn { get; init; }
    public required string IdColumnType { get; init; }
    public required string ColumnSuffix { get; init; }
    public required int HnswM { get; init; }
    public required int HnswEfConstruction { get; init; }
    public required List<VectorGroupDto> Groups { get; init; }
}

internal sealed record VectorGroupDto
{
    public required string Name { get; init; }
    public required int Dimensions { get; init; }
    public required List<VectorGroupPropertyDto> Properties { get; init; }

    public VectorGroup ToDomain() => new()
    {
        Name = Name,
        Dimensions = Dimensions,
        Properties = Properties.Select(p => p.ToDomain()).ToList(),
    };

    public static VectorGroupDto FromDomain(VectorGroup g) => new()
    {
        Name = g.Name,
        Dimensions = g.Dimensions,
        Properties = g.Properties.Select(VectorGroupPropertyDto.FromDomain).ToList(),
    };
}

internal sealed record VectorGroupPropertyDto
{
    public required string PropertyName { get; init; }
    public required string ColumnName { get; init; }
    public required string EmbeddingSource { get; init; }

    public VectorGroupProperty ToDomain() => new()
    {
        PropertyName = PropertyName,
        ColumnName = ColumnName,
        EmbeddingSource = EmbeddingSource,
    };

    public static VectorGroupPropertyDto FromDomain(VectorGroupProperty p) => new()
    {
        PropertyName = p.PropertyName,
        ColumnName = p.ColumnName,
        EmbeddingSource = p.EmbeddingSource,
    };
}
