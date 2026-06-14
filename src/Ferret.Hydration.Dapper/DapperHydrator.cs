using System.Data.Common;
using Dapper;
using Ferret.Abstractions;

namespace Ferret.Hydration.Dapper;

public sealed class DapperHydrator : IEntityHydrator
{
    public async Task<List<T>> HydrateAsync<T>(
        DbConnection connection,
        HydrationRequest request,
        CancellationToken ct) where T : class
    {
        var parameters = new DynamicParameters();
        for (var i = 0; i < request.Parameters.Count; i++) parameters.Add($"p{i}", request.Parameters[i]);

        // Translate `{0}` placeholders into `@p0` for Dapper.
        var sql = request.Sql;
        for (var i = 0; i < request.Parameters.Count; i++) sql = sql.Replace($"{{{i}}}", $"@p{i}");

        var rows = await connection.QueryAsync<T>(new CommandDefinition(sql, parameters, cancellationToken: ct));
        return rows.AsList();
    }
}
