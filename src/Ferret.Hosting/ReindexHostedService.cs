using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ferret.Hosting;

public sealed class ReindexHostedService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IReindexRunner _runner;
    private readonly ReindexHostedServiceOptions _options;
    private readonly ILogger<ReindexHostedService>? _logger;

    public ReindexHostedService(
        IServiceProvider services,
        IReindexRunner runner,
        IOptions<ReindexHostedServiceOptions> options,
        ILogger<ReindexHostedService>? logger = null)
    {
        _services = services;
        _runner = runner;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.SessionFactory is null)
        {
            throw new InvalidOperationException(
                "ReindexHostedServiceOptions.SessionFactory must be set so the worker can open a Ferret session each poll. " +
                "Configure it via AddFerretReindexHostedService(o => o.SessionFactory = ...).");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var session = await _options.SessionFactory(_services, stoppingToken);
                var drainOptions = new ReindexDrainOptions
                {
                    StaleClaimAfter    = _options.StaleClaimAfter,
                    BatchSizeOverride  = _options.BatchSizeOverride,
                    BatchDelayOverride = _options.BatchDelayOverride,
                };
                await _runner.DrainAsync(session, drainOptions, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Reindex worker drain failed; will retry after the poll interval.");
            }

            try
            {
                await Task.Delay(_options.PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
