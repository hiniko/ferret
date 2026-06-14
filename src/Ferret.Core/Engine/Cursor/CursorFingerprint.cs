using System.Security.Cryptography;
using System.Text;

namespace Ferret.Core.Engine.Cursor;

public static class CursorFingerprint
{
    public static string Compute(
        string tableName,
        IReadOnlyList<SortClause> sort,
        IReadOnlyList<FilterClause> filter,
        IReadOnlyList<string> keyColumns,
        string? searchTerm = null)
    {
        var sb = new StringBuilder();
        sb.Append(tableName).Append('|');
        foreach (var s in sort)
        {
            sb.Append(s.Field).Append(':').Append((int)s.Direction).Append(',');
        }
        sb.Append('|');
        foreach (var f in filter)
        {
            sb.Append(f.Field).Append(':').Append((int)f.Operator).Append(',');
        }
        sb.Append('|');
        foreach (var k in keyColumns)
        {
            sb.Append(k).Append(',');
        }
        if (!string.IsNullOrEmpty(searchTerm))
        {
            sb.Append('|').Append(searchTerm);
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }
}
