using System.Diagnostics;
using System.Globalization;
using Ferret.Abstractions.Embeddings;
using Ferret.Abstractions.Search;
using Ferret.Core.Backends.FullText;
using Ferret.Core.Backends.Vector;
using Ferret.Core.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NpgsqlTypes;

namespace Ferret.Core.Engine.Reindex;

internal readonly record struct ReindexJob(long Id, string Entity, string Group, int BatchSize, string? LastId, bool Targeted = false);

internal sealed class ReindexJobProcessor
{
    public async Task<int> DrainAsync(
        NpgsqlConnection connection,
        TimeSpan staleAfter,
        Func<ReindexJob, ReindexRangeRequest> resolve,
        CancellationToken ct,
        ILogger? logger = null,
        Func<ReindexJob, Task>? onJobClaimed = null)
    {
        logger ??= NullLogger.Instance;

        var candidates = await SelectCandidatesAsync(connection, staleAfter, ct);

        var processed = 0;
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();

            if (!await TryAcquireLockAsync(connection, candidate.Entity, candidate.Group, ct))
            {
                logger.LogDebug(
                    "Reindex advisory lock not acquired for {Entity}/{Group}; skipping.",
                    candidate.Entity, candidate.Group);
                continue;
            }

            ReindexJob? claimedJob = null;
            try
            {
                if (onJobClaimed is not null)
                    await onJobClaimed(candidate);

                var claimed = await ClaimAsync(connection, candidate.Entity, candidate.Group, ct);
                if (claimed is not { } job)
                    continue;
                claimedJob = job;

                using (var activity = FerretDiagnostics.ActivitySource.StartActivity(
                    "ferret.reindex.job", ActivityKind.Client))
                {
                    activity?.SetTag(FerretDiagnostics.Tags.Entity, job.Entity);

                    var request = resolve(job) with { StartAfterId = job.LastId, Targeted = job.Targeted, JobGroup = job.Group };
                    var lastId = await RunRangeAsync(
                        connection, request,
                        maxId => UpdateLastIdAsync(connection, job.Id, maxId, ct),
                        ct);

                    await MarkDoneAsync(connection, job.Id, lastId, ct);
                }

                processed++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex, "Reindex job for {Entity}/{Group} failed.",
                    candidate.Entity, candidate.Group);

                if (claimedJob is { } failed)
                    await MarkFailedAsync(connection, failed.Id, ex, ct);
            }
            finally
            {
                await ReleaseLockAsync(connection, candidate.Entity, candidate.Group, ct);
            }
        }

        return processed;
    }

    private static async Task<IReadOnlyList<ReindexJob>> SelectCandidatesAsync(
        NpgsqlConnection conn, TimeSpan staleAfter, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT "id", "entity", "group_name", "batch_size", "last_id"
            FROM "{FullTextDdlBuilder.ReindexJobsTable}"
            WHERE "status" IN ('pending', 'failed')
               OR ("status" = 'running' AND "started_at" < now() - @stale)
            ORDER BY "enqueued_at";
            """;
        cmd.Parameters.AddWithValue("stale", staleAfter);

        var rows = new List<ReindexJob>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new ReindexJob(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }
        return rows;
    }

    private static async Task<bool> TryAcquireLockAsync(
        NpgsqlConnection conn, string entity, string group, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT pg_try_advisory_lock(hashtext(@key));";
        cmd.Parameters.AddWithValue("key", ReindexLockKey.For(entity, group));
        return (bool)(await cmd.ExecuteScalarAsync(ct))!;
    }

    private static async Task ReleaseLockAsync(
        NpgsqlConnection conn, string entity, string group, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT pg_advisory_unlock(hashtext(@key));";
        cmd.Parameters.AddWithValue("key", ReindexLockKey.For(entity, group));
        await cmd.ExecuteScalarAsync(ct);
    }

    private static async Task<ReindexJob?> ClaimAsync(
        NpgsqlConnection conn, string entity, string group, CancellationToken ct)
    {
        await using var tx = await conn.BeginTransactionAsync(ct);

        long jobId;
        // A targeted job is a fresh per-owner enqueue from a change-tracking
        // trigger: still 'pending' and carrying the owner key in last_id. A full
        // backfill enqueue leaves last_id NULL; a resumed failed/running job is no
        // longer 'pending'. Only the targeted case reindexes its owner inclusively.
        bool targeted;
        await using (var pick = conn.CreateCommand())
        {
            pick.Transaction = tx;
            pick.CommandText = $"""
                SELECT "id", "status", "last_id"
                FROM "{FullTextDdlBuilder.ReindexJobsTable}"
                WHERE "entity" = @entity AND "group_name" = @group
                  AND ("status" = 'pending' OR "status" = 'running' OR "status" = 'failed')
                ORDER BY "id"
                LIMIT 1;
                """;
            pick.Parameters.AddWithValue("entity", entity);
            pick.Parameters.AddWithValue("group", group);
            await using var pickReader = await pick.ExecuteReaderAsync(ct);
            if (!await pickReader.ReadAsync(ct))
            {
                await pickReader.DisposeAsync();
                await tx.CommitAsync(ct);
                return null;
            }
            jobId = pickReader.GetInt64(0);
            targeted = pickReader.GetString(1) == "pending" && !pickReader.IsDBNull(2);
        }

        await using (var collapse = conn.CreateCommand())
        {
            collapse.Transaction = tx;
            // Collapse only duplicates that target the same owner key (or, for
            // full backfills, share the same NULL cursor). Distinct targeted owners
            // must each survive as their own claimable unit.
            collapse.CommandText = $"""
                DELETE FROM "{FullTextDdlBuilder.ReindexJobsTable}"
                WHERE "entity" = @entity AND "group_name" = @group
                  AND "id" <> @id
                  AND "last_id" IS NOT DISTINCT FROM
                      (SELECT "last_id" FROM "{FullTextDdlBuilder.ReindexJobsTable}" WHERE "id" = @id)
                  AND ("status" = 'pending' OR "status" = 'running' OR "status" = 'failed');
                """;
            collapse.Parameters.AddWithValue("entity", entity);
            collapse.Parameters.AddWithValue("group", group);
            collapse.Parameters.AddWithValue("id", jobId);
            await collapse.ExecuteNonQueryAsync(ct);
        }

        ReindexJob job;
        await using (var mark = conn.CreateCommand())
        {
            mark.Transaction = tx;
            mark.CommandText = $"""
                UPDATE "{FullTextDdlBuilder.ReindexJobsTable}"
                SET "status" = 'running', "started_at" = now(), "error" = NULL
                WHERE "id" = @id
                RETURNING "id", "entity", "group_name", "batch_size", "last_id";
                """;
            mark.Parameters.AddWithValue("id", jobId);
            await using var reader = await mark.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            job = new ReindexJob(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                targeted);
        }

        await tx.CommitAsync(ct);
        return job;
    }

    private static async Task UpdateLastIdAsync(
        NpgsqlConnection conn, long jobId, object maxId, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE "{FullTextDdlBuilder.ReindexJobsTable}"
            SET "last_id" = @last_id WHERE "id" = @id;
            """;
        cmd.Parameters.AddWithValue("last_id", Convert.ToString(maxId)!);
        cmd.Parameters.AddWithValue("id", jobId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task MarkDoneAsync(
        NpgsqlConnection conn, long jobId, object? lastId, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE "{FullTextDdlBuilder.ReindexJobsTable}"
            SET "status" = 'done', "finished_at" = now(), "last_id" = @last_id
            WHERE "id" = @id;
            """;
        cmd.Parameters.AddWithValue("last_id", lastId is null ? DBNull.Value : Convert.ToString(lastId)!);
        cmd.Parameters.AddWithValue("id", jobId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task MarkFailedAsync(
        NpgsqlConnection conn, long jobId, Exception ex, CancellationToken ct)
    {
        var message = ex.Message;
        if (message.Length > 4000)
            message = message[..4000];

        // A batch failure aborts the current transaction; roll it back so this
        // UPDATE runs on a clean connection state.
        if (conn.State == System.Data.ConnectionState.Open)
            await DiscardActiveTransactionAsync(conn, ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE "{FullTextDdlBuilder.ReindexJobsTable}"
            SET "status" = 'failed', "finished_at" = now(), "error" = @error
            WHERE "id" = @id;
            """;
        cmd.Parameters.AddWithValue("error", message);
        cmd.Parameters.AddWithValue("id", jobId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task DiscardActiveTransactionAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "ROLLBACK;";
        try { await cmd.ExecuteNonQueryAsync(ct); }
        catch (PostgresException) { /* no transaction in progress */ }
    }

    public async Task<object?> RunRangeAsync(
        NpgsqlConnection conn,
        ReindexRangeRequest request,
        Func<object, Task>? onBatchCommitted,
        CancellationToken ct)
    {
        if (request.Targeted && request.StartAfterId is not null)
            return await RunTargetedAsync(conn, request, ct);

        var keyColumns = KeyColumnsOf(request);
        if (keyColumns.Count > 1)
            return await RunCompositeRangeAsync(conn, request, keyColumns, onBatchCommitted, ct);

        var batchSql = FullTextDdlBuilder.BackfillBatch(
            request.SidecarTable, request.SidecarSchema,
            request.SourceTable,  request.SourceSchema,
            request.IdColumn, request.ColumnSuffix, request.Groups,
            greaterThan: true);

        // When seeding from min(id) the first batch must be inclusive (>=) to
        // index the min row; when resuming from a committed cursor it stays
        // exclusive (>) so the already-committed row is not reprocessed.
        var seedSql = request.StartAfterId is null
            ? FullTextDdlBuilder.BackfillBatch(
                request.SidecarTable, request.SidecarSchema,
                request.SourceTable,  request.SourceSchema,
                request.IdColumn, request.ColumnSuffix, request.Groups,
                greaterThan: false)
            : batchSql;

        object? lastId = await SeedLastIdAsync(conn, request, ct);
        if (lastId is null)
            return null;

        var first = true;
        object? result = request.StartAfterId;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var sql = first ? seedSql : batchSql;
            object? maxId;
            long affected;
            using (var activity = FerretDiagnostics.ActivitySource.StartActivity(
                "ferret.reindex.batch", ActivityKind.Client))
            {
                activity?.SetTag(FerretDiagnostics.Tags.Entity, request.SourceTable);

                await using var tx = await conn.BeginTransactionAsync(ct);
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = sql;
                if (lastId is string lastIdText)
                    cmd.Parameters.Add(new NpgsqlParameter("last_id", NpgsqlDbType.Unknown) { Value = lastIdText });
                else
                    cmd.Parameters.AddWithValue("last_id", lastId);
                cmd.Parameters.AddWithValue("batch_size", request.BatchSize);

                await using (var reader = await cmd.ExecuteReaderAsync(ct))
                {
                    await reader.ReadAsync(ct);
                    affected = reader.GetInt64(0);
                    maxId = affected == 0 ? null : reader.GetValue(1);
                }

                await tx.CommitAsync(ct);
                activity?.SetTag(FerretDiagnostics.Tags.RowCount, affected);
            }

            if (affected == 0)
                break;

            lastId = maxId!;
            result = maxId;
            first = false;
            if (onBatchCommitted is not null)
                await onBatchCommitted(maxId!);

            if (request.BatchDelay > TimeSpan.Zero)
                await Task.Delay(request.BatchDelay, ct);

            if (affected < request.BatchSize)
                break;
        }

        return result;
    }

    // Blocker #5: embeddings require an out-of-process model call, so they are
    // computed here with NO transaction held. Only the upsert in
    // RunVectorRangeAsync opens a transaction, after every row in the batch has
    // already been embedded.
    internal static async Task<IReadOnlyList<(object Id, float[] Vector)>> EmbedBatchAsync(
        IReadOnlyList<(object Id, string Text)> rows, IEmbeddingProvider provider, CancellationToken ct)
    {
        var result = new List<(object, float[])>(rows.Count);
        foreach (var (id, text) in rows)
            result.Add((id, await provider.EmbedAsync(text ?? string.Empty, ct)));
        return result;
    }

    public async Task<object?> RunVectorRangeAsync(
        NpgsqlConnection conn,
        ReindexRangeRequest request,
        IEmbeddingProvider provider,
        CancellationToken ct)
    {
        var group = request.VectorGroups![0];
        var sourceTextColumn = group.Properties[0].ColumnName;
        var embeddingColumn = VectorSidecarNaming.ColumnName(group.Name, request.ColumnSuffix, VectorSidecarNaming.CurrentVersion);

        var source = Qualify(request.SourceSchema, request.SourceTable);
        var sidecar = Qualify(request.SidecarSchema, request.SidecarTable);
        var idCol = QuoteIdentifier(request.IdColumn);
        var srcCol = QuoteIdentifier(sourceTextColumn);
        var embCol = QuoteIdentifier(embeddingColumn);

        var readSql =
            $"SELECT {idCol}, {srcCol} FROM {source} WHERE {idCol} > @last_id ORDER BY {idCol} LIMIT @batch_size;";
        var seedReadSql =
            $"SELECT {idCol}, {srcCol} FROM {source} WHERE {idCol} >= @last_id ORDER BY {idCol} LIMIT @batch_size;";

        var upsertSql =
            $"""
            INSERT INTO {sidecar} ({idCol}, {embCol}, "updated_at")
            VALUES (@id, @vec::vector, now())
            ON CONFLICT ({idCol}) DO UPDATE SET {embCol} = EXCLUDED.{embCol}, "updated_at" = now();
            """;

        object? lastId = await SeedLastIdAsync(conn, request, ct);
        if (lastId is null)
            return null;

        var first = true;
        object? result = request.StartAfterId;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            // Step 1: read a batch of source rows. NO transaction held.
            var rows = new List<(object Id, string Text)>(request.BatchSize);
            await using (var readCmd = conn.CreateCommand())
            {
                readCmd.CommandText = first ? seedReadSql : readSql;
                if (lastId is string lastIdText)
                    readCmd.Parameters.Add(new NpgsqlParameter("last_id", NpgsqlDbType.Unknown) { Value = lastIdText });
                else
                    readCmd.Parameters.AddWithValue("last_id", lastId);
                readCmd.Parameters.AddWithValue("batch_size", request.BatchSize);

                await using var reader = await readCmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    var id = reader.GetValue(0);
                    var text = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                    rows.Add((id, text));
                }
            }

            if (rows.Count == 0)
                break;

            // Step 3: compute embeddings out-of-process. NO transaction held.
            var embedded = await EmbedBatchAsync(rows, provider, ct);

            // Step 4: upsert the computed vectors inside a single transaction.
            object? maxId;
            using (var activity = FerretDiagnostics.ActivitySource.StartActivity(
                "ferret.reindex.batch", ActivityKind.Client))
            {
                activity?.SetTag(FerretDiagnostics.Tags.Entity, request.SourceTable);

                await using var tx = await conn.BeginTransactionAsync(ct);
                foreach (var (id, vec) in embedded)
                {
                    await using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = upsertSql;
                    cmd.Parameters.AddWithValue("id", id);
                    cmd.Parameters.Add(new NpgsqlParameter("vec", NpgsqlDbType.Unknown) { Value = FormatVector(vec) });
                    await cmd.ExecuteNonQueryAsync(ct);
                }
                await tx.CommitAsync(ct);
                activity?.SetTag(FerretDiagnostics.Tags.RowCount, embedded.Count);
            }

            maxId = embedded[^1].Id;
            lastId = maxId;
            result = maxId;
            first = false;

            if (request.BatchDelay > TimeSpan.Zero)
                await Task.Delay(request.BatchDelay, ct);

            if (rows.Count < request.BatchSize)
                break;
        }

        return result;
    }

    private static string FormatVector(float[] vector)
    {
        var parts = new string[vector.Length];
        for (var i = 0; i < vector.Length; i++)
            parts[i] = vector[i].ToString(CultureInfo.InvariantCulture);
        return "[" + string.Join(",", parts) + "]";
    }

    private static string QuoteIdentifier(string identifier) =>
        $"\"{identifier.Replace("\"", "\"\"")}\"";

    private static string Qualify(string? schema, string table) =>
        schema is null
            ? $"\"{table.Replace("\"", "\"\"")}\""
            : $"\"{schema.Replace("\"", "\"\"")}\".\"{table.Replace("\"", "\"\"")}\"";

    private static IReadOnlyList<string> KeyColumnsOf(ReindexRangeRequest request) =>
        request.KeyColumns is { Count: > 0 } cols ? cols : [request.IdColumn];

    // Composite keys carry the resume/targeted cursor as a '|'-joined text value
    // (matching the change-tracking enqueue encoding) so it round-trips through the
    // jobs table's single text "last_id" column.
    private const char CompositeKeySeparator = '|';

    // String key parts may themselves contain the '|' separator (or the '\' escape),
    // so each part is escaped ('\' -> '\\', '|' -> '\|') before joining with '|'.
    // The change-tracking trigger SQL applies the byte-identical replace() chain so
    // both encoders agree, and decode splits only on UNESCAPED '|' to recover parts.
    internal static string EncodeCompositeKey(IReadOnlyList<object> parts) =>
        string.Join(CompositeKeySeparator, parts.Select(p => EscapePart(Convert.ToString(p)!)));

    internal static string[] DecodeCompositeKey(string encoded, int arity)
    {
        var parts = new string[arity];
        var current = new System.Text.StringBuilder();
        var index = 0;
        for (var i = 0; i < encoded.Length; i++)
        {
            var c = encoded[i];
            if (c == '\\' && i + 1 < encoded.Length)
            {
                current.Append(encoded[i + 1]);
                i++;
            }
            else if (c == CompositeKeySeparator && index < arity - 1)
            {
                parts[index++] = current.ToString();
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        parts[index] = current.ToString();
        return parts;
    }

    private static string EscapePart(string part) =>
        part.Replace("\\", "\\\\").Replace("|", "\\|");

    private async Task<object?> RunCompositeRangeAsync(
        NpgsqlConnection conn,
        ReindexRangeRequest request,
        IReadOnlyList<string> keyColumns,
        Func<object, Task>? onBatchCommitted,
        CancellationToken ct)
    {
        var arity = keyColumns.Count;

        var batchSql = FullTextDdlBuilder.BackfillBatch(
            request.SidecarTable, request.SidecarSchema,
            request.SourceTable,  request.SourceSchema,
            keyColumns, request.ColumnSuffix, request.Groups,
            greaterThan: true);

        // First batch is inclusive (>=) when seeding from the minimum composite key
        // so the min row is indexed; resuming from a committed cursor stays exclusive.
        var seedSql = request.StartAfterId is null
            ? FullTextDdlBuilder.BackfillBatch(
                request.SidecarTable, request.SidecarSchema,
                request.SourceTable,  request.SourceSchema,
                keyColumns, request.ColumnSuffix, request.Groups,
                greaterThan: false)
            : batchSql;

        string[]? lastKey = request.StartAfterId is string resume
            ? DecodeCompositeKey(resume, arity)
            : await SeedCompositeKeyAsync(conn, request, keyColumns, ct);
        if (lastKey is null)
            return null;

        var first = true;
        object? result = request.StartAfterId;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var sql = first ? seedSql : batchSql;
            object[]? maxKey = null;
            long affected;
            using (var activity = FerretDiagnostics.ActivitySource.StartActivity(
                "ferret.reindex.batch", ActivityKind.Client))
            {
                activity?.SetTag(FerretDiagnostics.Tags.Entity, request.SourceTable);

                await using var tx = await conn.BeginTransactionAsync(ct);
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = sql;
                // Bind each row-value part as text (Unknown): Postgres resolves the
                // type from the (k1,k2) > (@last_id0,@last_id1) comparison context.
                for (var i = 0; i < arity; i++)
                    cmd.Parameters.Add(new NpgsqlParameter($"last_id{i}", NpgsqlDbType.Unknown) { Value = lastKey[i] });
                cmd.Parameters.AddWithValue("batch_size", request.BatchSize);

                await using (var reader = await cmd.ExecuteReaderAsync(ct))
                {
                    await reader.ReadAsync(ct);
                    affected = reader.GetInt64(0);
                    if (affected > 0)
                    {
                        maxKey = new object[arity];
                        for (var i = 0; i < arity; i++)
                            maxKey[i] = reader.GetValue(i + 1);
                    }
                }

                await tx.CommitAsync(ct);
                activity?.SetTag(FerretDiagnostics.Tags.RowCount, affected);
            }

            if (affected == 0)
                break;

            var encoded = EncodeCompositeKey(maxKey!);
            lastKey = DecodeCompositeKey(encoded, arity);
            result = encoded;
            first = false;
            if (onBatchCommitted is not null)
                await onBatchCommitted(encoded);

            if (request.BatchDelay > TimeSpan.Zero)
                await Task.Delay(request.BatchDelay, ct);

            if (affected < request.BatchSize)
                break;
        }

        return result;
    }

    private static async Task<object?> RunTargetedAsync(
        NpgsqlConnection conn,
        ReindexRangeRequest request,
        CancellationToken ct)
    {
        // Reindex exactly the enqueued owner: inclusive (>=) from the owner key,
        // bounded to a single row so the change-tracking refresh touches only its
        // owner and never advances a resume cursor.
        var keyColumns = KeyColumnsOf(request);
        var sql = FullTextDdlBuilder.BackfillBatch(
            request.SidecarTable, request.SidecarSchema,
            request.SourceTable,  request.SourceSchema,
            keyColumns, request.ColumnSuffix, request.Groups,
            greaterThan: false);

        using var activity = FerretDiagnostics.ActivitySource.StartActivity(
            "ferret.reindex.batch", ActivityKind.Client);
        activity?.SetTag(FerretDiagnostics.Tags.Entity, request.SourceTable);

        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        if (keyColumns.Count > 1)
        {
            // The owner key was enqueued as a '|'-joined composite key; split it back
            // into the N row-value parts the composite batch SQL binds.
            var parts = DecodeCompositeKey(Convert.ToString(request.StartAfterId)!, keyColumns.Count);
            for (var i = 0; i < keyColumns.Count; i++)
                cmd.Parameters.Add(new NpgsqlParameter($"last_id{i}", NpgsqlDbType.Unknown) { Value = parts[i] });
        }
        else
        {
            cmd.Parameters.Add(new NpgsqlParameter("last_id", NpgsqlDbType.Unknown)
            {
                Value = Convert.ToString(request.StartAfterId)!,
            });
        }
        cmd.Parameters.AddWithValue("batch_size", 1);

        long affected;
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            await reader.ReadAsync(ct);
            affected = reader.GetInt64(0);
        }

        await tx.CommitAsync(ct);
        activity?.SetTag(FerretDiagnostics.Tags.RowCount, affected);

        return request.StartAfterId;
    }

    private static async Task<object?> SeedLastIdAsync(
        NpgsqlConnection conn, ReindexRangeRequest request, CancellationToken ct)
    {
        if (request.StartAfterId is not null)
            return request.StartAfterId;

        var source = request.SourceSchema is null
            ? $"\"{request.SourceTable.Replace("\"", "\"\"")}\""
            : $"\"{request.SourceSchema.Replace("\"", "\"\"")}\".\"{request.SourceTable.Replace("\"", "\"\"")}\"";
        var id = $"\"{request.IdColumn.Replace("\"", "\"\"")}\"";

        // Use ORDER BY ... LIMIT 1 rather than min(): min() has no aggregate for some
        // key types (e.g. uuid), whereas the btree ordering works for any sortable key.
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {id} FROM {source} ORDER BY {id} ASC LIMIT 1;";
        var min = await cmd.ExecuteScalarAsync(ct);
        return min is null or DBNull ? null : min;
    }

    private static async Task<string[]?> SeedCompositeKeyAsync(
        NpgsqlConnection conn,
        ReindexRangeRequest request,
        IReadOnlyList<string> keyColumns,
        CancellationToken ct)
    {
        var source = request.SourceSchema is null
            ? $"\"{request.SourceTable.Replace("\"", "\"\"")}\""
            : $"\"{request.SourceSchema.Replace("\"", "\"\"")}\".\"{request.SourceTable.Replace("\"", "\"\"")}\"";
        var cols = string.Join(", ", keyColumns.Select(c => $"\"{c.Replace("\"", "\"\"")}\""));

        // ORDER BY the full key in declaration order so the seed matches the keyset
        // ordering the batch advances over; ORDER BY ... LIMIT 1 (not min()) supports
        // any orderable key type (e.g. uuid).
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {cols} FROM {source} ORDER BY {cols} ASC LIMIT 1;";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct) || reader.IsDBNull(0))
            return null;

        var parts = new string[keyColumns.Count];
        for (var i = 0; i < keyColumns.Count; i++)
            parts[i] = Convert.ToString(reader.GetValue(i))!;
        return parts;
    }
}
