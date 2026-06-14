using System.CommandLine;
using Ferret.Core.Backends.FullText;
using Npgsql;

namespace Ferret.Tools.Cli;

internal static class ReindexStatusCommand
{
    public static Command Create(ReindexCliContext context)
    {
        var entityOption = new Option<string>("--entity") { Required = true };
        var groupOption = new Option<string?>("--group");
        var connectionOption = new Option<string?>("--connection");
        var assemblyOption = new Option<string[]>("--assembly") { AllowMultipleArgumentsPerToken = false };

        var command = new Command("reindex-status", "Print the reindex job rows for an entity.")
        {
            entityOption,
            groupOption,
            connectionOption,
            assemblyOption,
        };

        command.SetAction(async (parseResult, ct) =>
        {
            var entity = parseResult.GetValue(entityOption)!;
            var group = parseResult.GetValue(groupOption);
            var connectionString = CliConnection.Resolve(parseResult.GetValue(connectionOption));

            var output = parseResult.InvocationConfiguration.Output;
            var error = parseResult.InvocationConfiguration.Error;

            if (connectionString is null)
            {
                error.WriteLine("No connection string. Pass --connection or set FERRET_CONNECTION.");
                return 2;
            }

            if (!context.TryResolveTable(entity, out var table))
            {
                error.WriteLine($"Unknown entity '{entity}'. No [SearchableEntity] type with that name was found.");
                return 1;
            }

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync(ct);

            await using (var ensure = conn.CreateCommand())
            {
                ensure.CommandText = FullTextDdlBuilder.EnsureReindexJobsTable();
                await ensure.ExecuteNonQueryAsync(ct);
            }

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                SELECT "id", "group_name", "status", "batch_size", "last_id", "enqueued_at", "started_at", "finished_at", "error"
                FROM "{FullTextDdlBuilder.ReindexJobsTable}"
                WHERE "entity" = @entity
                  AND (@group IS NULL OR "group_name" = @group)
                ORDER BY "id";
                """;
            cmd.Parameters.AddWithValue("entity", table);
            cmd.Parameters.AddWithValue("group", (object?)group ?? DBNull.Value);

            output.WriteLine($"Reindex jobs for {entity} ({table}):");
            var any = false;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                any = true;
                var id = reader.GetInt64(0);
                var groupName = reader.GetString(1);
                var status = reader.GetString(2);
                var batchSize = reader.GetInt32(3);
                var lastId = reader.IsDBNull(4) ? "-" : reader.GetString(4);
                output.WriteLine($"  #{id} {groupName} status={status} batch_size={batchSize} last_id={lastId}");
            }

            if (!any)
                output.WriteLine("  (no jobs)");

            return 0;
        });

        return command;
    }
}
