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

    // ---- filtered search ----
    // HNSW is approximate and cannot see a WHERE clause: Postgres applies the filter to whatever
    // the index already chose, so a selective filter silently returns a short page. See
    // docs/vector-filtering.md. These settings control how Ferret avoids that.

    /// <summary>
    /// How a vector search combined with a filter is executed. Default <see cref="VectorFilterStrategy.Auto"/>.
    /// </summary>
    public VectorFilterStrategy FilterStrategy { get; set; } = VectorFilterStrategy.Auto;

    /// <summary>
    /// Under <see cref="VectorFilterStrategy.Auto"/>, a filter estimated to match at most this many
    /// rows is executed as an exact scan instead of through the HNSW index — exact results, and
    /// typically faster than the index once the candidate set is this small. Raise it if your
    /// vectors are small or your latency budget is generous; lower it if exact scans are too slow.
    /// </summary>
    public int ExactScanThreshold { get; set; } = 10_000;

    /// <summary>
    /// Hard ceiling on the exact-scan path. The estimate comes from Postgres statistics and can be
    /// badly wrong when they are stale (measured 144x low right after a bulk insert), so a query
    /// routed to an exact scan is capped here: exceed it and Ferret falls back to the index path
    /// and warns, rather than quietly scanning far more than intended. Enforced as a time budget
    /// (a statement timeout on the exact-scan query) rather than a literal row-count comparison —
    /// this value only labels the fallback warning, it is not counted against directly.
    /// </summary>
    public int MaxExactScanRows { get; set; } = 250_000;

    /// <summary>
    /// pgvector 0.8 iterative scan mode, applied via SET LOCAL when a filtered search takes the
    /// index path. Lets the index keep pulling candidates when the filter discards them, at the
    /// cost of latency. Requires pgvector 0.8+; <see cref="HnswIterativeScan.Off"/> to disable.
    /// </summary>
    public HnswIterativeScan IterativeScan { get; set; } = HnswIterativeScan.RelaxedOrder;

    /// <summary>
    /// Caps how many tuples an iterative scan will pull before giving up. pgvector's own default
    /// is 20,000. Only applies when <see cref="IterativeScan"/> is enabled.
    /// </summary>
    public int MaxScanTuples { get; set; } = 20_000;
}

/// <summary>How Ferret executes a vector search that also carries a filter.</summary>
public enum VectorFilterStrategy
{
    /// <summary>
    /// Estimate the filter's selectivity and choose: exact scan when it is selective, the HNSW
    /// index when it is not. Costs one planner round trip per filtered query.
    /// </summary>
    Auto,

    /// <summary>
    /// Always exact-scan the filtered set, never using the index. Correct by construction and
    /// independent of statistics freshness — the right choice when vector searches are always
    /// tenant-scoped or otherwise selective. Still bounded by <see cref="VectorOptions.MaxExactScanRows"/>.
    /// </summary>
    AlwaysExact,

    /// <summary>
    /// Always use the HNSW index. The behaviour before routing existed: fastest, but a selective
    /// filter can return fewer rows than requested.
    /// </summary>
    AlwaysIndex,
}

/// <summary>pgvector <c>hnsw.iterative_scan</c> modes.</summary>
public enum HnswIterativeScan
{
    /// <summary>No iterative scanning (pgvector default).</summary>
    Off,

    /// <summary>Keep exact distance ordering while scanning further.</summary>
    StrictOrder,

    /// <summary>Allow approximate ordering for better recall. pgvector's recommended default.</summary>
    RelaxedOrder,
}
