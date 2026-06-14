using Ferret.Abstractions.Embeddings;
using Microsoft.Extensions.AI;

namespace Ferret.Core.Embeddings;

/// <summary>
/// Adapts a Microsoft.Extensions.AI embedding generator to Ferret's <see cref="IEmbeddingProvider"/>.
/// Dimensions and model id are supplied explicitly (MEAI does not always expose them on the generator).
/// The adapter does not take ownership of the generator; the caller (typically DI) manages its lifetime.
/// </summary>
public sealed class MeaiEmbeddingProvider : IEmbeddingProvider
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _generator;

    public int Dimensions { get; }
    public string ModelId { get; }

    public MeaiEmbeddingProvider(
        IEmbeddingGenerator<string, Embedding<float>> generator, string modelId, int dimensions)
    {
        _generator = generator;
        ModelId = modelId;
        Dimensions = dimensions;
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct)
    {
        var result = await _generator.GenerateAsync(new[] { text }, options: null, ct);
        if (result.Count == 0)
            throw new InvalidOperationException(
                $"Embedding model '{ModelId}' returned no embeddings.");

        var vector = result[0].Vector.ToArray();

        if (vector.Length != Dimensions)
            throw new InvalidOperationException(
                $"Embedding model '{ModelId}' returned {vector.Length} dimensions but {Dimensions} were configured. " +
                "Check the configured dimensions or the model.");

        return vector;
    }
}
