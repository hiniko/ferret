namespace Ferret.Abstractions.Sql;

/// <summary>
/// Dialect-specific SQL formatting. **Public to enable testing and possible future providers**,
/// but consumers should not implement this themselves — it is treated as internal.
/// </summary>
public interface ISqlDialect
{
    string QuoteIdentifier(string name);
    string PagingClause(int limit, int offset);
    string CountOverWindow();
    string ArrayParameter(string parameterName);
}
