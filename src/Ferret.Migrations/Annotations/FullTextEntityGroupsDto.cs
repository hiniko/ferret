namespace Ferret.Migrations.Annotations;

/// <summary>
/// Internal JSON DTO that round-trips <see cref="FullTextGroup"/> state through the EF Core
/// model snapshot via the <see cref="SearchableAnnotationKeys.FullTextGroupsV1"/> annotation.
/// </summary>
internal sealed record FullTextEntityGroupsDto
{
    public required string SidecarTable { get; init; }
    public string? SidecarSchema { get; init; }
    public required string SourceTable { get; init; }
    public string? SourceSchema { get; init; }
    public required string IdColumn { get; init; }
    public required string IdColumnType { get; init; }
    public required string ColumnSuffix { get; init; }
    public List<FullTextKeyPartDto> KeyParts { get; init; } = [];
    public required List<FullTextGroupDto> Groups { get; init; }
}

internal sealed record FullTextKeyPartDto
{
    public required string ColumnName { get; init; }
    public required string ColumnType { get; init; }
}

internal sealed record FullTextGroupDto
{
    public required string Name { get; init; }
    public required string FullTextConfig { get; init; }
    public required ReindexMode Reindex { get; init; }
    public string? PreviousGroup { get; init; }
    public required List<FullTextGroupPropertyDto> Properties { get; init; }

    public FullTextGroup ToDomain() => new()
    {
        Name = Name,
        FullTextConfig = FullTextConfig,
        Reindex = Reindex,
        Properties = Properties.Select(p => p.ToDomain()).ToList(),
    };

    public static FullTextGroupDto FromDomain(FullTextGroup g) => new()
    {
        Name = g.Name,
        FullTextConfig = g.FullTextConfig,
        Reindex = g.Reindex,
        Properties = g.Properties.Select(FullTextGroupPropertyDto.FromDomain).ToList(),
    };
}

internal sealed record FullTextGroupPropertyDto
{
    public required string PropertyName { get; init; }
    public required string ColumnName { get; init; }
    public required FullTextWeightBucket Weight { get; init; }
    public string? FullTextConfigOverride { get; init; }
    public FullTextJoinPathDto? Join { get; init; }

    public FullTextGroupProperty ToDomain() => new()
    {
        PropertyName = PropertyName,
        ColumnName = ColumnName,
        Weight = Weight,
        FullTextConfigOverride = FullTextConfigOverride,
        Join = Join?.ToDomain(),
    };

    public static FullTextGroupPropertyDto FromDomain(FullTextGroupProperty p) => new()
    {
        PropertyName = p.PropertyName,
        ColumnName = p.ColumnName,
        Weight = p.Weight,
        FullTextConfigOverride = p.FullTextConfigOverride,
        Join = p.Join is null ? null : FullTextJoinPathDto.FromDomain(p.Join),
    };
}

internal sealed record FullTextJoinPathDto
{
    public required List<FullTextJoinHopDto> Hops { get; init; }

    public JoinPath ToDomain() => new()
    {
        Hops = Hops.Select(h => h.ToDomain()).ToList(),
    };

    public static FullTextJoinPathDto FromDomain(JoinPath path) => new()
    {
        Hops = path.Hops.Select(FullTextJoinHopDto.FromDomain).ToList(),
    };
}

internal sealed record FullTextJoinHopDto
{
    public required string TableName { get; init; }
    public required string TableAlias { get; init; }
    public required string ForeignKeyColumn { get; init; }
    public string? Schema { get; init; }
    public required JoinCardinality Cardinality { get; init; }
    public required bool ForeignKeyOwningSide { get; init; }
    public string ReferencedKeyColumn { get; init; } = "id";

    public JoinHop ToDomain() => new()
    {
        TableName = TableName,
        TableAlias = TableAlias,
        ForeignKeyColumn = ForeignKeyColumn,
        Schema = Schema,
        EntityType = typeof(object),
        Cardinality = Cardinality,
        ForeignKeyOwningSide = ForeignKeyOwningSide,
        ReferencedKeyColumn = ReferencedKeyColumn,
    };

    public static FullTextJoinHopDto FromDomain(JoinHop h) => new()
    {
        TableName = h.TableName,
        TableAlias = h.TableAlias,
        ForeignKeyColumn = h.ForeignKeyColumn,
        Schema = h.Schema,
        Cardinality = h.Cardinality,
        ForeignKeyOwningSide = h.ForeignKeyOwningSide,
        ReferencedKeyColumn = h.ReferencedKeyColumn,
    };
}
