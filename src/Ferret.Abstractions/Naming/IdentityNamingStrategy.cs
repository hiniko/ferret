using System.Reflection;

namespace Ferret.Abstractions.Naming;

/// <summary>Returns CLR names verbatim. Useful for legacy schemas with quoted PascalCase identifiers.</summary>
public sealed class IdentityNamingStrategy : INamingStrategy
{
    public string TableName(Type entityType) => entityType.Name;
    public string ColumnName(PropertyInfo property) => property.Name;
}
