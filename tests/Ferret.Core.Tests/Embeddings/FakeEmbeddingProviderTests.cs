using Ferret.Core.Embeddings;
using FluentAssertions;
using Xunit;

namespace Ferret.Core.Tests.Embeddings;

public class FakeEmbeddingProviderTests
{
    [Fact]
    public async Task Same_text_yields_same_vector_of_requested_dimension()
    {
        var p = new FakeEmbeddingProvider(dimensions: 8);
        var a = await p.EmbedAsync("hello world", default);
        var b = await p.EmbedAsync("hello world", default);

        a.Should().HaveCount(8);
        a.Should().Equal(b);
    }

    [Fact]
    public async Task Output_is_unit_normalized()
    {
        var p = new FakeEmbeddingProvider(dimensions: 8);
        var v = await p.EmbedAsync("anything", default);
        var norm = Math.Sqrt(v.Sum(x => (double)x * x));
        norm.Should().BeApproximately(1.0, 1e-6);
    }

    [Fact]
    public async Task Different_text_yields_different_vector()
    {
        var p = new FakeEmbeddingProvider(dimensions: 8);
        (await p.EmbedAsync("cat", default)).Should().NotEqual(await p.EmbedAsync("dog", default));
    }
}
