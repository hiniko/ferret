using System.Data.Common;
using Ferret.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Ferret.EntityFrameworkCore;

public sealed class EntityFrameworkHydrator : IEntityHydrator
{
    private readonly DbContext _context;

    public EntityFrameworkHydrator(DbContext context) => _context = context;

    public async Task<List<T>> HydrateAsync<T>(
        DbConnection connection,
        HydrationRequest request,
        CancellationToken ct) where T : class
    {
        var sql = RewriteProjection<T>(request.Sql);
#pragma warning disable EF1002    // metadata values, not user input
        var query = _context.Set<T>().FromSqlRaw(sql, request.Parameters.ToArray()).AsNoTracking();
#pragma warning restore EF1002
        return await query.ToListAsync(ct);
    }

    /// <summary>
    /// Replaces the engine's <c>SELECT *</c> with an explicit column list derived from the EF model for
    /// <typeparamref name="T"/>. This ensures system columns such as PostgreSQL's <c>xmin</c>
    /// (used by Npgsql <c>.IsRowVersion()</c>) are included in the <c>FromSqlRaw</c> subquery so
    /// EF's outer projection can reference them.
    /// </summary>
    private string RewriteProjection<T>(string engineSql) where T : class
    {
        var entityType = _context.Model.FindEntityType(typeof(T))
            ?? throw new InvalidOperationException($"Entity {typeof(T).Name} is not registered in the EF model.");

        // Resolve the store object (table) for this entity so we use the table-scoped column name.
        var storeObject = StoreObjectIdentifier.Create(entityType, StoreObjectType.Table);

        // Enumerate every mapped scalar property → column name. Include all columns (incl. xmin).
        var columns = entityType.GetProperties()
            .Select(p => storeObject is { } s ? p.GetColumnName(s) : p.GetColumnName())
            .Where(c => !string.IsNullOrEmpty(c))
            .Distinct()
            .Select(c => $"\"{c}\"");

        var cols = string.Join(", ", columns);

        // engineSql is always "SELECT * FROM <table> WHERE ..." — keep everything from FROM onward.
        var fromIdx = engineSql.IndexOf("FROM", StringComparison.Ordinal);
        if (fromIdx < 0)
            return engineSql; // unexpected shape — fall back to original

        return $"SELECT {cols} {engineSql[fromIdx..]}";
    }
}
