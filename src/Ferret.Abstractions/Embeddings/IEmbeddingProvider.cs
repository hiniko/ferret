namespace Ferret.Abstractions.Embeddings;

/// <summary>Resolves source text to a fixed-dimension embedding vector.</summary>
public interface IEmbeddingProvider
{
    /// <summary>Dimensionality of vectors this provider returns.</summary>
    int Dimensions { get; }

    /// <summary>Identifier of the model producing the vectors (recorded for mismatch detection).</summary>
    string ModelId { get; }

    Task<float[]> EmbedAsync(string text, CancellationToken ct);
}
