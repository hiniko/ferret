using Ferret.Abstractions.Search;

namespace Ferret.Core.Engine.Reindex;

internal sealed record ReindexRangeRequest
{
    public required string SidecarTable { get; init; }
    public string? SidecarSchema { get; init; }
    public required string SourceTable { get; init; }
    public string? SourceSchema { get; init; }
    public required string IdColumn { get; init; }

    /// <summary>
    /// The full ordered key-part column list (declaration order = sidecar column
    /// order = keyset tiebreaker order). When null, falls back to the single
    /// <see cref="IdColumn"/> (the single-key fast path).
    /// </summary>
    public IReadOnlyList<string>? KeyColumns { get; init; }

    public required string ColumnSuffix { get; init; }
    public required IReadOnlyList<FullTextGroup> Groups { get; init; }

    /// <summary>
    /// When set, this range is a vector backfill: <see cref="Groups"/> is empty and
    /// the embeddings are computed out-of-process before each write transaction.
    /// </summary>
    public IReadOnlyList<VectorGroup>? VectorGroups { get; init; }
    public required int BatchSize { get; init; }
    public TimeSpan BatchDelay { get; init; }
    public object? StartAfterId { get; init; }

    /// <summary>
    /// The reindex job's group name. Null for fulltext-only ranges.
    /// </summary>
    public string? JobGroup { get; init; }

    /// <summary>
    /// When true, <see cref="StartAfterId"/> is an exact owner key to reindex
    /// inclusively (a per-owner refresh enqueued by a change-tracking trigger),
    /// not a resume cursor to advance past.
    /// </summary>
    public bool Targeted { get; init; }
}
