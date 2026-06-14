using System.Net.Http.Json;
using Ferret.Abstractions.Embeddings;

namespace Ferret.Core.Embeddings;

/// <summary>
/// Embedding provider backed by an OpenAI-compatible /v1/embeddings HTTP endpoint
/// (OpenAI, Azure, Ollama /v1, Groq, etc.). The HttpClient must carry the base address
/// and any Authorization header.
/// </summary>
public sealed class OpenAiEmbeddingProvider : IEmbeddingProvider
{
    private readonly HttpClient _http;
    private readonly bool _sendDimensionsParam;

    public int Dimensions { get; }
    public string ModelId { get; }

    public OpenAiEmbeddingProvider(HttpClient http, string model, int dimensions, bool sendDimensionsParam = true)
    {
        _http = http;
        ModelId = model;
        Dimensions = dimensions;
        _sendDimensionsParam = sendDimensionsParam;
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct)
    {
        object payload = _sendDimensionsParam
            ? new { input = text, model = ModelId, dimensions = Dimensions }
            : new { input = text, model = ModelId };

        var response = await _http.PostAsJsonAsync("/v1/embeddings", payload, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(cancellationToken: ct);
        if (body is null || body.Data.Count == 0)
            throw new InvalidOperationException(
                $"Embedding model '{ModelId}' returned no embedding data (empty or missing 'data'). " +
                "The endpoint may have returned an error with a 200 status.");

        var vector = body.Data[0].Embedding;

        if (vector.Length != Dimensions)
            throw new InvalidOperationException(
                $"Embedding model '{ModelId}' returned {vector.Length} dimensions but {Dimensions} were configured. " +
                "Check the configured dimensions or the model.");

        return vector;
    }

    private sealed record EmbeddingResponse(List<EmbeddingDatum> Data);
    private sealed record EmbeddingDatum(float[] Embedding);
}
