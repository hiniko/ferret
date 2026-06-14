using Ferret.Abstractions.Session;
using Ferret.Core.Backends.FullText;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Ferret.Core.Engine.Reindex;

internal sealed class ReindexRunner : IReindexRunner
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(5);

    private readonly EntityRegistry _registry;
    private readonly FullTextOptions _options;
    private readonly ILogger<ReindexRunner>? _logger;

    public ReindexRunner(
        EntityRegistry registry,
        FullTextOptions options,
        ILogger<ReindexRunner>? logger = null)
    {
        _registry = registry;
        _options = options;
        _logger = logger;
    }

    public Task<int> DrainAsync(IFerretSession session, CancellationToken ct)
        => DrainAsync(session, new ReindexDrainOptions(), ct);

    public async Task<int> DrainAsync(IFerretSession session, ReindexDrainOptions options, CancellationToken ct = default)
    {
        var connection = (NpgsqlConnection)await session.OpenConnectionAsync(ct);

        var staleAfter = options.StaleClaimAfter ?? StaleAfter;

        var processor = new ReindexJobProcessor();
        return await processor.DrainAsync(
            connection,
            staleAfter,
            job => Resolve(job, options),
            ct,
            _logger);
    }

    private ReindexRangeRequest Resolve(ReindexJob job, ReindexDrainOptions options)
    {
        var model = _registry.All.FirstOrDefault(m => m.TableName == job.Entity)
            ?? throw new InvalidOperationException(
                $"No registered entity maps to reindex job entity '{job.Entity}'.");

        var sidecarTable = FullTextSidecarNaming.TableName(model.TableName, _options);

        return new ReindexRangeRequest
        {
            SidecarTable  = sidecarTable,
            SidecarSchema = _options.SidecarSchema,
            SourceTable   = model.TableName,
            SourceSchema  = model.Schema,
            IdColumn      = model.KeyColumnName,
            ColumnSuffix  = _options.ColumnSuffix,
            Groups        = model.FullTextGroups,
            BatchSize     = options.BatchSizeOverride ?? job.BatchSize,
            BatchDelay    = options.BatchDelayOverride ?? _options.ConcurrentBatchDelay,
        };
    }
}
