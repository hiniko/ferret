using Ferret.Core.Embeddings;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Xunit;

namespace Ferret.Core.Tests.Embeddings;

public class MeaiEmbeddingProviderTests
{
    private sealed class StubGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken ct = default)
        {
            var result = new GeneratedEmbeddings<Embedding<float>>(
                new[] { new Embedding<float>(new float[] { 0.1f, 0.2f, 0.3f }) });
            return Task.FromResult(result);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    [Fact]
    public async Task Maps_generator_output_to_float_array_and_reports_model_and_dims()
    {
        var provider = new MeaiEmbeddingProvider(new StubGenerator(), modelId: "nomic-embed-text", dimensions: 3);

        provider.Dimensions.Should().Be(3);
        provider.ModelId.Should().Be("nomic-embed-text");
        (await provider.EmbedAsync("hi", default)).Should().Equal(0.1f, 0.2f, 0.3f);
    }
}
