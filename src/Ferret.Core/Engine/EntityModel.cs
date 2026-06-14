using Ferret.Abstractions.Search;

namespace Ferret.Core.Engine;

public sealed class EntityModel
{
    public required Type ClrType { get; init; }
    public required string TableName { get; init; }
    public string? Schema { get; init; }
    public required IReadOnlyList<KeyPart> Key { get; init; }
    public bool IsComposite => Key.Count > 1;
    public string KeyPropertyName => Key[0].PropertyName;
    public string KeyColumnName => Key[0].ColumnName;
    public Type KeyClrType => Key[0].ClrType;
    public required IReadOnlyDictionary<string, string> ColumnByPropertyName { get; init; }
    public required IReadOnlyDictionary<string, Type> ClrTypeByPropertyName { get; init; }
    public required IReadOnlyList<SearchablePropertyInfo> SearchableProperties { get; init; }
    public IReadOnlyList<FullTextGroup> FullTextGroups { get; init; } = [];
    public IReadOnlyDictionary<string, string?> FullTextGroupRenames { get; init; }
        = new Dictionary<string, string?>(StringComparer.Ordinal);
    public IReadOnlyList<VectorGroup> VectorGroups { get; init; } = [];
    public HybridConfig? HybridConfig { get; init; }
    public required IReadOnlyDictionary<string, FilterableAttribute> Filterable { get; init; }
    public required IReadOnlySet<string> Sortable { get; init; }

    public string QuotedTable(ISqlDialect dialect) => string.IsNullOrEmpty(Schema)
        ? dialect.QuoteIdentifier(TableName)
        : $"{dialect.QuoteIdentifier(Schema!)}.{dialect.QuoteIdentifier(TableName)}";
}
