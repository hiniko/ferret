using System.Net.Http.Headers;
using Ferret.Abstractions.Embeddings;
using Ferret.Core.Embeddings;
using Ferret.Core.IntegrationTests;
using FluentAssertions;
using Xunit;

namespace Ferret.Core.IntegrationTests.Vector;

public class VectorLiveProviderTests
{
    [SkippableFact]
    public async Task Live_provider_embeds_text()
    {
        BenchGate.SkipUnlessEnabled();
        var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        Skip.If(string.IsNullOrEmpty(key), "OPENAI_API_KEY not set");

        using var http = new HttpClient { BaseAddress = new Uri("https://api.openai.com") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        var provider = new OpenAiEmbeddingProvider(http, "text-embedding-3-small", 1536);

        var vec = await provider.EmbedAsync("hello world", default);

        vec.Should().HaveCount(1536);
        vec.Sum(x => Math.Abs(x)).Should().BeGreaterThan(0);
    }
}
