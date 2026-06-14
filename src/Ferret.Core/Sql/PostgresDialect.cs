namespace Ferret.Core.Sql;

/// <summary>PostgreSQL implementation of <see cref="ISqlDialect"/>. The only dialect shipped in v1.</summary>
public sealed class PostgresDialect : ISqlDialect
{
    public string QuoteIdentifier(string name) =>
        $"\"{name.Replace("\"", "\"\"")}\"";

    public string PagingClause(int limit, int offset) =>
        $"LIMIT {limit} OFFSET {offset}";

    public string CountOverWindow() => "COUNT(*) OVER()";

    public string ArrayParameter(string parameterName) => $"ANY({parameterName})";
}
