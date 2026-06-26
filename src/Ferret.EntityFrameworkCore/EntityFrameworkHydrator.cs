using System.Data.Common;
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
    /// Replaces the engine's leading <c>SELECT *</c> with an explicit column list derived
    /// from the EF model for <typeparamref name="T"/>. This ensures system columns such as
    /// PostgreSQL's <c>xmin</c> (used by Npgsql <c>.IsRowVersion()</c>) are included in the
    /// <c>FromSqlRaw</c> subquery so EF's outer projection can reference them. Owned-type
    /// properties mapped to the same store object (OwnsOne / table splitting) are collected
    /// recursively so their columns are not omitted.
    /// </summary>
    internal string RewriteProjection<T>(string engineSql) where T : class
    {
        var entityType = _context.Model.FindEntityType(typeof(T))
            ?? throw new InvalidOperationException($"Entity {typeof(T).Name} is not registered in the EF model.");

        // Resolve the store object for this entity: try table first, then view.
        // Throws if the entity is mapped to neither (e.g. keyless query type with no mapping).
        var store = StoreObjectIdentifier.Create(entityType, StoreObjectType.Table)
                 ?? StoreObjectIdentifier.Create(entityType, StoreObjectType.View)
                 ?? throw new InvalidOperationException(
                     $"Ferret cannot hydrate '{typeof(T)}': entity is mapped to neither a table nor a view.");

        // Collect column names from this entity and all owned navigations on the same store object.
        // Owned collections (OwnsMany) mapped to a separate table return null from GetColumnName(store)
        // and are naturally skipped — they are not columns on this table.
        var cols = string.Join(", ", MappedColumns(entityType, store).Distinct().Select(c => $"\"{c}\""));

        // The engine SQL always begins "SELECT * FROM <table> WHERE ...".  Only the leading
        // SELECT * is rewritten here.  A global Replace would corrupt the composite-key
        // hydration SQL, which contains an inner "SELECT * FROM unnest(...)" in its WHERE clause.
        var fromIdx = engineSql.IndexOf("FROM", StringComparison.Ordinal);
        if (fromIdx < 0)
            return engineSql; // unexpected shape — fall back to original

        return $"SELECT {cols} {engineSql[fromIdx..]}";
    }

    /// <summary>
    /// Returns the column names mapped to <paramref name="store"/> for <paramref name="entityType"/>
    /// and, recursively, for any owned navigations that share the same store object.
    /// </summary>
    private static IEnumerable<string> MappedColumns(IEntityType entityType, StoreObjectIdentifier store)
    {
        foreach (var p in entityType.GetProperties())
        {
            var c = p.GetColumnName(store);
            if (!string.IsNullOrEmpty(c)) yield return c;
        }

        // Owned types mapped into the SAME store object (table splitting / OwnsOne).
        foreach (var nav in entityType.GetNavigations())
        {
            var target = nav.TargetEntityType;
            if (target.IsOwned())
                foreach (var c in MappedColumns(target, store))
                    yield return c;
        }
    }
}
