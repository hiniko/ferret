using Ferret.Core.Backends.FullText;

namespace Ferret.Core.Engine;

public sealed class ReindexOptions
{
    public int? BatchSize { get; set; }
    public TimeSpan? BatchDelay { get; set; }
    public string? StartAfterId { get; set; }

    public (int BatchSize, TimeSpan BatchDelay) Resolve(FullTextOptions fullText)
        => (BatchSize ?? fullText.ConcurrentBatchSize, BatchDelay ?? fullText.ConcurrentBatchDelay);
}
