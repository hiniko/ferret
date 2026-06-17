using Ferret.Core.Embeddings;
using Ferret.Core.IntegrationTests.Fixtures;
using FluentAssertions;
using Xunit;

namespace Ferret.Core.IntegrationTests.Vector;

[Collection("ollama")]
public class OllamaEmbeddingGateTests
{
    private readonly OllamaFixture _ollama;
    public OllamaEmbeddingGateTests(OllamaFixture ollama) => _ollama = ollama;

    private OpenAiEmbeddingProvider Provider() => new(
        _ollama.CreateHttpClient(), OllamaFixture.Model, OllamaFixture.Dimensions, sendDimensionsParam: false);

    private static double Cosine(float[] a, float[] b)
    {
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++) { dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
        return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }

    [Fact]
    public async Task Embeds_real_768d_vectors_with_semantic_signal()
    {
        var provider = Provider();

        var cat = await provider.EmbedAsync("a small domestic cat", default);
        var kitten = await provider.EmbedAsync("a young kitten", default);
        var finance = await provider.EmbedAsync("quarterly tax accounting report", default);

        cat.Should().HaveCount(768);
        Cosine(cat, kitten).Should().BeGreaterThan(Cosine(cat, finance));
    }
}
