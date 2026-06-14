using System.Reflection;
using System.Text;

namespace Ferret.Abstractions.Naming;

/// <summary>
/// Converts <c>PascalCase</c> CLR names to <c>snake_case</c> database identifiers and
/// pluralises table names (e.g. <c>Product</c> → <c>products</c>).
/// </summary>
public sealed class SnakeCaseNamingStrategy : INamingStrategy
{
    public string TableName(Type entityType) => Pluralise(ToSnakeCase(entityType.Name));

    public string ColumnName(PropertyInfo property) => ToSnakeCase(property.Name);

    private static string ToSnakeCase(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var sb = new StringBuilder(input.Length + 4);
        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (char.IsUpper(c))
            {
                if (i > 0 && (char.IsLower(input[i - 1]) || (i + 1 < input.Length && char.IsLower(input[i + 1]))))
                {
                    sb.Append('_');
                }
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private static string Pluralise(string singular)
    {
        if (singular.EndsWith('s')) return singular;
        if (singular.EndsWith('y')) return singular[..^1] + "ies";
        return singular + "s";
    }
}
