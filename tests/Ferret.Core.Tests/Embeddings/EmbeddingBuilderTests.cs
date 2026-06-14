using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ferret.Abstractions.Embeddings;
using Ferret.Core.Backends.Vector;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ferret.Core.Tests.Embeddings;

public class EmbeddingBuilderTests
{
    private sealed class StubGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(
                new[] { new Embedding<float>(new float[768]) }));

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    [Fact]
    public void UseOpenAiEmbeddings_sets_factory_producing_a_provider()
    {
        var opts = new VectorOptions();
        opts.UseOpenAiEmbeddings(apiKey: "sk-test", model: "text-embedding-3-small", dimensions: 1536);

        opts.EmbeddingProviderFactory.Should().NotBeNull();

        var services = new ServiceCollection();
        services.AddHttpClient();
        opts.ApplyHttpClientRegistration(services);
        var sp = services.BuildServiceProvider();

        var provider = opts.EmbeddingProviderFactory!(sp);
        provider.Should().BeOfType<Ferret.Core.Embeddings.OpenAiEmbeddingProvider>();
        provider.Dimensions.Should().Be(1536);
        provider.ModelId.Should().Be("text-embedding-3-small");
    }

    [Fact]
    public void UseOllamaEmbeddings_defaults_to_nomic_768_and_no_dimensions_param()
    {
        var opts = new VectorOptions();
        opts.UseOllamaEmbeddings(endpoint: "http://localhost:11434");

        var services = new ServiceCollection();
        services.AddHttpClient();
        opts.ApplyHttpClientRegistration(services);
        var sp = services.BuildServiceProvider();

        var provider = opts.EmbeddingProviderFactory!(sp);
        provider.Dimensions.Should().Be(768);
        provider.ModelId.Should().Be("nomic-embed-text");
    }

    [Fact]
    public void UseEmbeddingGenerator_sets_factory_and_requires_no_http_registration()
    {
        var opts = new VectorOptions();
        opts.UseEmbeddingGenerator(_ => new StubGenerator(), modelId: "nomic-embed-text", dimensions: 768);

        opts.EmbeddingProviderFactory.Should().NotBeNull();

        var services = new ServiceCollection();
        opts.ApplyHttpClientRegistration(services); // must be a no-op for the MEAI path
        var sp = services.BuildServiceProvider();

        var provider = opts.EmbeddingProviderFactory!(sp);
        provider.Should().BeOfType<Ferret.Core.Embeddings.MeaiEmbeddingProvider>();
        provider.ModelId.Should().Be("nomic-embed-text");
        provider.Dimensions.Should().Be(768);
    }
}
