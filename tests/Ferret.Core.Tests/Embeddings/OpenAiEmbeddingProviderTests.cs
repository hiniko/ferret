using System.Net;
using System.Text;
using System.Text.Json;
using Ferret.Core.Embeddings;
using FluentAssertions;
using Xunit;

namespace Ferret.Core.Tests.Embeddings;

public class OpenAiEmbeddingProviderTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }
        private readonly float[] _vector;
        public CapturingHandler(float[] vector) => _vector = vector;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Body = await request.Content!.ReadAsStringAsync(ct);
            var payload = JsonSerializer.Serialize(new { data = new[] { new { embedding = _vector } } });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
        }
    }

    [Fact]
    public async Task Sends_dimensions_param_when_enabled()
    {
        var handler = new CapturingHandler(new float[3]);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com") };
        var provider = new OpenAiEmbeddingProvider(http, "text-embedding-3-small", 3, sendDimensionsParam: true);

        await provider.EmbedAsync("hi", default);

        handler.Body.Should().Contain("\"dimensions\":3");
        provider.ModelId.Should().Be("text-embedding-3-small");
    }

    [Fact]
    public async Task Omits_dimensions_param_when_disabled()
    {
        var handler = new CapturingHandler(new float[768]);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        var provider = new OpenAiEmbeddingProvider(http, "nomic-embed-text", 768, sendDimensionsParam: false);

        await provider.EmbedAsync("hi", default);

        handler.Body.Should().NotContain("dimensions");
    }

    [Fact]
    public async Task Throws_when_returned_length_mismatches_configured_dimensions()
    {
        var handler = new CapturingHandler(new float[5]);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com") };
        var provider = new OpenAiEmbeddingProvider(http, "m", 3, sendDimensionsParam: true);

        var act = () => provider.EmbedAsync("hi", default);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*returned 5 dimensions but 3 were configured*");
    }
}
