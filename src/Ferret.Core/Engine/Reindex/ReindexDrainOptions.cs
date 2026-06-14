namespace Ferret.Core.Engine.Reindex;

public sealed class ReindexDrainOptions
{
    public TimeSpan? StaleClaimAfter { get; set; }

    public int? BatchSizeOverride { get; set; }

    public TimeSpan? BatchDelayOverride { get; set; }
}
