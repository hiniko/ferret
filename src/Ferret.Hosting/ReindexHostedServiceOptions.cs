namespace Ferret.Hosting;

public sealed class ReindexHostedServiceOptions
{
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan? StaleClaimAfter { get; set; }

    public int? BatchSizeOverride { get; set; }

    public TimeSpan? BatchDelayOverride { get; set; }

    public Func<IServiceProvider, CancellationToken, Task<IFerretSession>>? SessionFactory { get; set; }
}
