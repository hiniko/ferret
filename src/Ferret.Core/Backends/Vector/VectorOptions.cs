using System;
using System.Net.Http;
using Ferret.Abstractions.Embeddings;
using Ferret.Core.Embeddings;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Ferret.Core.Backends.Vector;

public sealed class VectorOptions
{
    // Shared by all HTTP embedding connectors (OpenAI-compatible + Ollama). v1 has a single
    // active embedding config per registration, so one named client cannot collide.
    internal const string EmbeddingsHttpClientName = "ferret-embeddings";

    internal Func<IServiceProvider, IEmbeddingProvider>? EmbeddingProviderFactory { get; private set; }

    // Set by UseOpenAiEmbeddings/UseOllamaEmbeddings so DI can register the named HttpClient.
    private Action<IServiceCollection>? _httpClientRegistration;

    public VectorOptions UseEmbeddingProvider(Func<IServiceProvider, IEmbeddingProvider> factory)
    {
        EmbeddingProviderFactory = factory;
        _httpClientRegistration = null;
        return this;
    }

    public VectorOptions UseOpenAiEmbeddings(
        string apiKey,
        string model,
        int dimensions,
        string endpoint = "https://api.openai.com",
        bool sendDimensionsParam = true)
    {
        // AddHttpClient(name, ...) self-registers IHttpClientFactory, so no separate AddHttpClient() is needed.
        _httpClientRegistration = services =>
            services.AddHttpClient(EmbeddingsHttpClientName, http =>
            {
                http.BaseAddress = new Uri(endpoint);
                if (!string.IsNullOrEmpty(apiKey))
                    http.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            });

        EmbeddingProviderFactory = sp =>
        {
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(EmbeddingsHttpClientName);
            return new OpenAiEmbeddingProvider(http, model, dimensions, sendDimensionsParam);
        };
        return this;
    }

    public VectorOptions UseOllamaEmbeddings(
        string endpoint,
        string model = "nomic-embed-text",
        int dimensions = 768)
        => UseOpenAiEmbeddings(apiKey: "", model: model, dimensions: dimensions,
            endpoint: endpoint, sendDimensionsParam: false);

    public VectorOptions UseEmbeddingGenerator(
        Func<IServiceProvider, IEmbeddingGenerator<string, Embedding<float>>> factory,
        string modelId,
        int dimensions)
    {
        _httpClientRegistration = null;
        EmbeddingProviderFactory = sp =>
            new MeaiEmbeddingProvider(factory(sp), modelId, dimensions);
        return this;
    }

    /// <summary>Invoked by DI to register any named HttpClient the chosen connector needs.</summary>
    internal void ApplyHttpClientRegistration(IServiceCollection services)
        => _httpClientRegistration?.Invoke(services);

    public string SidecarSuffix { get; set; } = "_vec";
    public string ColumnSuffix { get; set; } = "_embedding";
    public string? SidecarSchema { get; set; }
    public bool AsPrimary { get; set; }
    /// <summary>HNSW build parameter (graph degree).</summary>
    public int HnswM { get; set; } = 16;
    /// <summary>HNSW build parameter (construction candidate list size).</summary>
    public int HnswEfConstruction { get; set; } = 64;
    /// <summary>Query-time HNSW candidate list size (recall/latency tradeoff). Applied via SET LOCAL.</summary>
    public int EfSearch { get; set; } = 40;
    public int ConcurrentBatchSize { get; set; } = 1000;
    public TimeSpan ConcurrentBatchDelay { get; set; } = TimeSpan.Zero;
}
