namespace Ferret.Core.Backends.Trigram;

public sealed class TrigramOptions
{
    /// <summary>Minimum word_similarity threshold for a field to count as a match. Default 0.35.</summary>
    public double MinimumSimilarity { get; set; } = 0.35;

    /// <summary>Computed: maximum &lt;&lt;-&gt; distance.</summary>
    public double MaxDistance => 1 - MinimumSimilarity;
}
