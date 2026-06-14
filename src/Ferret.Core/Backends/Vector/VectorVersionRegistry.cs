using Npgsql;

namespace Ferret.Core.Backends.Vector;

/// <summary>
/// Reads/writes the ferret_vector_versions registry and enforces the fail-loud guards.
/// v1: exactly one active row per (entity, group), version = VectorSidecarNaming.CurrentVersion.
/// </summary>
public static class VectorVersionRegistry
{
    public static void EnsureConfigDims(int providerDims, int columnDims, string entity, string group)
    {
        if (providerDims != columnDims)
            throw new InvalidOperationException(
                $"Embedding provider dimensions ({providerDims}) do not match {entity}.{group} embedding column " +
                $"dimensions ({columnDims}). Fix the UseXxxEmbeddings(dimensions:) value or the " +
                "[Searchable(EmbeddingDimensions=)] attribute.");
    }

    public static void EnsureMatch(
        VectorVersionRow? active, string entity, string group, string configuredModel, int configuredDims)
    {
        if (active is null)
            throw new InvalidOperationException(
                $"No active embedding version for {entity}.{group}; run reindex (engine.ReindexAsync) first.");

        if (active.Dimensions != configuredDims)
            throw new InvalidOperationException(
                $"Embedding dimensions changed for {entity}.{group} (stored v{active.VersionId}={active.Dimensions}, " +
                $"configured={configuredDims}); reindex required.");

        if (!string.Equals(active.Model, configuredModel, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Embedding model changed for {entity}.{group} (stored v{active.VersionId}='{active.Model}', " +
                $"configured='{configuredModel}'); reindex required.");
    }

    public static async Task<VectorVersionRow?> GetActiveAsync(
        NpgsqlConnection connection, string entity, string group, CancellationToken ct)
    {
        const string sql = """
            SELECT version_id, entity, group_name, model, dimensions, column_name, status
            FROM ferret_vector_versions
            WHERE entity = @entity AND group_name = @group AND status = 'active'
            ORDER BY version_id DESC
            LIMIT 1;
            """;

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@entity", entity);
        cmd.Parameters.AddWithValue("@group", group);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        return new VectorVersionRow
        {
            VersionId = reader.GetInt64(0),
            Entity = reader.GetString(1),
            GroupName = reader.GetString(2),
            Model = reader.GetString(3),
            Dimensions = reader.GetInt32(4),
            ColumnName = reader.GetString(5),
            Status = reader.GetString(6),
        };
    }

    /// <summary>
    /// Upsert the single active row for (entity, group) at the current version. Idempotent:
    /// updates model/dimensions/column_name if a row already exists, else inserts one.
    /// </summary>
    public static async Task UpsertActiveAsync(
        NpgsqlConnection connection, string entity, string group,
        string model, int dimensions, string columnName, CancellationToken ct)
    {
        const string update = """
            UPDATE ferret_vector_versions
            SET model = @model, dimensions = @dims, column_name = @col, status = 'active'
            WHERE entity = @entity AND group_name = @group AND status = 'active';
            """;
        const string insert = """
            INSERT INTO ferret_vector_versions (entity, group_name, model, dimensions, column_name, status)
            VALUES (@entity, @group, @model, @dims, @col, 'active');
            """;

        await using (var up = new NpgsqlCommand(update, connection))
        {
            up.Parameters.AddWithValue("@entity", entity);
            up.Parameters.AddWithValue("@group", group);
            up.Parameters.AddWithValue("@model", model);
            up.Parameters.AddWithValue("@dims", dimensions);
            up.Parameters.AddWithValue("@col", columnName);
            if (await up.ExecuteNonQueryAsync(ct) > 0) return;
        }

        await using var ins = new NpgsqlCommand(insert, connection);
        ins.Parameters.AddWithValue("@entity", entity);
        ins.Parameters.AddWithValue("@group", group);
        ins.Parameters.AddWithValue("@model", model);
        ins.Parameters.AddWithValue("@dims", dimensions);
        ins.Parameters.AddWithValue("@col", columnName);
        await ins.ExecuteNonQueryAsync(ct);
    }
}
