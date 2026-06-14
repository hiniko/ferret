namespace Ferret.Abstractions.Attributes;

/// <summary>Supported search backends. Each backend ships in its own opt-in module.</summary>
public enum SearchBackend
{
    /// <summary>pg_trgm trigram similarity search via GiST + <c>&lt;&lt;-&gt;</c>.</summary>
    Trigram,

    /// <summary>PostgreSQL full-text search via tsvector/tsquery + GIN.</summary>
    FullText,

    /// <summary>pgvector cosine/L2/inner-product nearest neighbour with HNSW.</summary>
    Vector
}

/// <summary>
/// Marks a property as searchable. Multiple <see cref="SearchableAttribute"/> values are allowed
/// on a single property to participate in hybrid scoring.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true, Inherited = false)]
public sealed class SearchableAttribute : Attribute
{
    /// <summary>Which backend handles this property.</summary>
    public SearchBackend Backend { get; init; } = SearchBackend.Trigram;

    /// <summary>
    /// Relative weight. Defaults to 1.0. Values &lt;1 down-weight, values &gt;1 up-weight.
    /// Ranking is relative across the searchable fields participating in a query — only ratios matter.
    /// </summary>
    public float Weight { get; init; } = 1.0f;

    /// <summary>Full-text only: PostgreSQL text-search configuration name (e.g. <c>english</c>, <c>simple</c>).</summary>
    public string? FullTextConfig { get; init; }

    /// <summary>Vector only: dimensionality of the embedding column.</summary>
    public int EmbeddingDimensions { get; init; }

    /// <summary>Vector only: name of a sibling property that holds the source text for write-side embedding.</summary>
    public string? EmbeddingSource { get; init; }

    /// <summary>
    /// Logical group name. Properties with the same Group share one sidecar column.
    /// Null/empty → "default". Fulltext-only. Trigram ignores it.
    /// </summary>
    public string? Group { get; init; }

    /// <summary>
    /// Rename hint: the group this property previously belonged to. Lets a migration
    /// detect a group rename rather than a drop + add. Fulltext-only.
    /// </summary>
    public string? PreviousGroup { get; init; }
}

/// <summary>Reindex behaviour for a fulltext group when the schema changes.</summary>
public enum ReindexMode
{
    /// <summary>Migration emits backfill SQL inside the same transaction.</summary>
    Inline,
    /// <summary>Migration emits schema only; backfill runs via job table.</summary>
    Concurrent,
    /// <summary>Migration emits schema only; backfill is the caller's problem.</summary>
    Deferred,
}

/// <summary>tsquery parser used to interpret incoming Search strings.</summary>
public enum FullTextParser
{
    /// <summary>websearch_to_tsquery — handles quoted phrases, OR, -exclude.</summary>
    Websearch,
    /// <summary>plainto_tsquery — bag of words.</summary>
    Plain,
    /// <summary>phraseto_tsquery — single phrase.</summary>
    Phrase,
    /// <summary>to_tsquery — caller-controlled tsquery syntax.</summary>
    Raw,
}

/// <summary>Combinator for multi-group ts_rank_cd scores.</summary>
public enum GroupCombinator
{
    /// <summary>GREATEST across group ranks.</summary>
    Max,
    /// <summary>Sum of group ranks.</summary>
    Sum,
}
