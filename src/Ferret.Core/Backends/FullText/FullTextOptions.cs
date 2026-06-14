using Ferret.Abstractions.Attributes;

namespace Ferret.Core.Backends.FullText;

public sealed class FullTextOptions
{
    public string DefaultConfig { get; set; } = "simple";
    public ReindexMode DefaultReindex { get; set; } = ReindexMode.Inline;
    public FullTextParser DefaultParser { get; set; } = FullTextParser.Websearch;
    public GroupCombinator GroupCombinator { get; set; } = GroupCombinator.Max;
    public string SidecarSuffix { get; set; } = "_search";
    public string ColumnSuffix { get; set; } = "_tsv";
    public string? SidecarSchema { get; set; }
    public (float A, float B, float C) WeightBuckets { get; set; } = (2.0f, 1.0f, 0.5f);
    public bool AsPrimary { get; set; }
    public int ConcurrentBatchSize { get; set; } = 5000;
    public TimeSpan ConcurrentBatchDelay { get; set; } = TimeSpan.Zero;
}
