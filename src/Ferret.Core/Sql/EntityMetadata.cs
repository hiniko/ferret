using Ferret.Core.Engine;

namespace Ferret.Core.Sql;

public sealed class EntityMetadata
{
    public required string TableName { get; init; }
    public string? Schema { get; init; }
    public required IReadOnlyList<KeyPart> Key { get; init; }
    public bool IsComposite => Key.Count > 1;
    public string IdColumnName => Key[0].ColumnName;
    public string KeyPropertyName => Key[0].PropertyName;
    public required string QuotedTable { get; init; }
    public required IReadOnlyDictionary<string, string> ColumnByPropertyName { get; init; }
    public required IReadOnlyDictionary<string, Type> ClrTypeByPropertyName { get; init; }
    public required IReadOnlyDictionary<string, FilterableAttribute> Filterable { get; init; }
    public required IReadOnlySet<string> Sortable { get; init; }
    public required ISqlDialect Dialect { get; init; }

    public static EntityMetadata From(EntityModel model, ISqlDialect dialect) => new()
    {
        TableName = model.TableName,
        Schema = model.Schema,
        Key = [.. model.Key],
        QuotedTable = model.QuotedTable(dialect),
        ColumnByPropertyName = model.ColumnByPropertyName,
        ClrTypeByPropertyName = model.ClrTypeByPropertyName,
        Filterable = model.Filterable,
        Sortable = model.Sortable,
        Dialect = dialect,
    };
}
