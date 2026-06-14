using System.Reflection;

namespace Ferret.Abstractions.Naming;

/// <summary>
/// Resolves CLR-to-database identifiers in standalone mode. EF mode bypasses this
/// and uses <c>IEntityType</c>/<c>IProperty</c> column metadata directly.
/// </summary>
public interface INamingStrategy
{
    string TableName(Type entityType);
    string ColumnName(PropertyInfo property);
}
