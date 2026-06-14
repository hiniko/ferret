using System.CommandLine;
using Ferret.Core.Backends.FullText;
using Ferret.Core.Engine.Reindex;
using Npgsql;

namespace Ferret.Tools.Cli;

internal static class ReindexCommand
{
    public static Command Create(ReindexCliContext context)
    {
        var entityOption = new Option<string>("--entity") { Required = true };
        var groupOption = new Option<string>("--group") { Required = true };
        var batchSizeOption = new Option<int>("--batch-size") { DefaultValueFactory = _ => 1000 };
        var sleepMsOption = new Option<int>("--sleep-ms") { DefaultValueFactory = _ => 0 };
        var connectionOption = new Option<string?>("--connection");
        var assemblyOption = new Option<string[]>("--assembly") { AllowMultipleArgumentsPerToken = false };

        var command = new Command("reindex", "Enqueue a pending reindex job (if none exists) and drain it to completion.")
        {
            entityOption,
            groupOption,
            batchSizeOption,
            sleepMsOption,
            connectionOption,
            assemblyOption,
        };

        command.SetAction(async (parseResult, ct) =>
        {
            var entity = parseResult.GetValue(entityOption)!;
            var group = parseResult.GetValue(groupOption)!;
            var batchSize = parseResult.GetValue(batchSizeOption);
            var sleepMs = parseResult.GetValue(sleepMsOption);
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

            await EnqueueIfMissingAsync(connectionString, table, group, batchSize, ct);

            var options = new ReindexDrainOptions
            {
                BatchSizeOverride = batchSize,
                BatchDelayOverride = sleepMs > 0 ? TimeSpan.FromMilliseconds(sleepMs) : null,
            };

            await using var session = new CliSession(connectionString, context.Dialect);
            var processed = await context.Runner.DrainAsync(session, options, ct);

            output.WriteLine($"Drained {processed} reindex job(s) for {entity} ({table}/{group}).");
            return 0;
        });

        return command;
    }

    private static async Task EnqueueIfMissingAsync(
        string connectionString, string table, string group, int batchSize, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);

        await using (var ensure = conn.CreateCommand())
        {
            ensure.CommandText = FullTextDdlBuilder.EnsureReindexJobsTable();
            await ensure.ExecuteNonQueryAsync(ct);
        }

        await using var insert = conn.CreateCommand();
        insert.CommandText = $"""
            INSERT INTO "{FullTextDdlBuilder.ReindexJobsTable}" ("entity", "group_name", "status", "batch_size")
            SELECT @entity, @group, 'pending', @batch_size
            WHERE NOT EXISTS (
                SELECT 1 FROM "{FullTextDdlBuilder.ReindexJobsTable}"
                WHERE "entity" = @entity AND "group_name" = @group
                  AND "status" IN ('pending', 'running', 'failed')
            );
            """;
        insert.Parameters.AddWithValue("entity", table);
        insert.Parameters.AddWithValue("group", group);
        insert.Parameters.AddWithValue("batch_size", batchSize);
        await insert.ExecuteNonQueryAsync(ct);
    }
}
