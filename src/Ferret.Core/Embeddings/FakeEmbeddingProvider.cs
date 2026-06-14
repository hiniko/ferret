using System.Security.Cryptography;
using System.Text;
using Ferret.Abstractions.Embeddings;

namespace Ferret.Core.Embeddings;

/// <summary>
/// Deterministic, offline embedding provider for tests and local development.
/// Hashes the input text into a stable pseudo-random unit vector of the requested dimension.
/// </summary>
public sealed class FakeEmbeddingProvider : IEmbeddingProvider
{
    public int Dimensions { get; }
    public string ModelId => "fake";

    public FakeEmbeddingProvider(int dimensions) => Dimensions = dimensions;

    public Task<float[]> EmbedAsync(string text, CancellationToken ct)
    {
        var vec = new float[Dimensions];
        var seed = SHA256.HashData(Encoding.UTF8.GetBytes(text ?? string.Empty));
        for (var i = 0; i < Dimensions; i++)
        {
            var b0 = seed[(i * 2) % seed.Length];
            var b1 = seed[(i * 2 + 1) % seed.Length];
            var raw = (short)((b0 << 8) | b1);
            vec[i] = raw / 32768f;
        }
        var norm = (float)Math.Sqrt(vec.Sum(x => (double)x * x));
        if (norm > 0)
            for (var i = 0; i < Dimensions; i++) vec[i] /= norm;
        return Task.FromResult(vec);
    }
}
