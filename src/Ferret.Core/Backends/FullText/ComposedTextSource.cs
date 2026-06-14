using Ferret.Abstractions.Search;

namespace Ferret.Core.Backends.FullText;

public enum ComposedColumnKind
{
    OwnerLocal = 0,
    Scalar = 1,
    Aggregated = 2,
}

public sealed record ComposedColumn
{
    public required string PropertyName { get; init; }
    public required string ColumnName { get; init; }
    public required FullTextWeightBucket Weight { get; init; }
    public required ComposedColumnKind Kind { get; init; }
    public required string Alias { get; init; }
    public string? FullTextConfigOverride { get; init; }
}

public sealed record ComposedJoin
{
    public required string TableName { get; init; }
    public string? Schema { get; init; }
    public required string Alias { get; init; }
    public required string OnClause { get; init; }
    public required JoinCardinality Cardinality { get; init; }
}

public sealed record ComposedTextSource
{
    public const string OwnerAlias = "e";

    public required IReadOnlyList<ComposedColumn> Columns { get; init; }
    public required IReadOnlyList<ComposedJoin> Joins { get; init; }
    public required IReadOnlyList<string> GroupByKeys { get; init; }

    public static ComposedTextSource Build(
        FullTextGroup group,
        string ownerTable,
        string? ownerSchema,
        IReadOnlyList<string> ownerKeyColumns)
    {
        _ = ownerTable;
        _ = ownerSchema;

        var columns = new List<ComposedColumn>(group.Properties.Count);
        var joins = new List<ComposedJoin>();
        var seenAliases = new HashSet<string>(StringComparer.Ordinal);

        foreach (var prop in group.Properties)
        {
            var hops = prop.Join?.Hops ?? [];
            if (hops.Count == 0)
            {
                columns.Add(new ComposedColumn
                {
                    PropertyName = prop.PropertyName,
                    ColumnName = prop.ColumnName,
                    Weight = prop.Weight,
                    Kind = ComposedColumnKind.OwnerLocal,
                    Alias = OwnerAlias,
                    FullTextConfigOverride = prop.FullTextConfigOverride,
                });
                continue;
            }

            var prevAlias = OwnerAlias;
            string? prevReferencedKey = null;
            var aggregated = false;
            for (var i = 0; i < hops.Count; i++)
            {
                var hop = hops[i];
                if (hop.Cardinality == JoinCardinality.OneToMany)
                {
                    aggregated = true;
                }

                if (seenAliases.Add(hop.TableAlias))
                {
                    joins.Add(new ComposedJoin
                    {
                        TableName = hop.TableName,
                        Schema = hop.Schema,
                        Alias = hop.TableAlias,
                        Cardinality = hop.Cardinality,
                        OnClause = BuildOnClause(hop, prevAlias, prevReferencedKey, ownerKeyColumns),
                    });
                }

                prevAlias = hop.TableAlias;
                prevReferencedKey = hop.ReferencedKeyColumn;
            }

            columns.Add(new ComposedColumn
            {
                PropertyName = prop.PropertyName,
                ColumnName = prop.ColumnName,
                Weight = prop.Weight,
                Kind = aggregated ? ComposedColumnKind.Aggregated : ComposedColumnKind.Scalar,
                Alias = hops[^1].TableAlias,
                FullTextConfigOverride = prop.FullTextConfigOverride,
            });
        }

        return new ComposedTextSource
        {
            Columns = columns,
            Joins = joins,
            GroupByKeys = ownerKeyColumns,
        };
    }

    private static string BuildOnClause(
        JoinHop hop, string prevAlias, string? prevReferencedKey, IReadOnlyList<string> ownerKeyColumns)
    {
        var prevKey = prevAlias == OwnerAlias
            ? (ownerKeyColumns.Count > 0 ? ownerKeyColumns[0] : "id")
            : prevReferencedKey ?? "id";

        return hop.ForeignKeyOwningSide
            ? $"{Q(hop.TableAlias)}.{Q(hop.ReferencedKeyColumn)} = {Q(prevAlias)}.{Q(hop.ForeignKeyColumn)}"
            : $"{Q(hop.TableAlias)}.{Q(hop.ForeignKeyColumn)} = {Q(prevAlias)}.{Q(prevKey)}";
    }

    private static string Q(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";
}
