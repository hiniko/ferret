using Ferret.Abstractions.Embeddings;
using Ferret.Core.Engine.Reindex;
using FluentAssertions;
using Xunit;

namespace Ferret.Core.Tests.Engine.Reindex;

public class VectorBackfillTests
{
    private sealed class SpyProvider : IEmbeddingProvider
    {
        public List<string> Seen { get; } = [];
        public int Dimensions => 4;
        public string ModelId => "spy";
        public Task<float[]> EmbedAsync(string text, CancellationToken ct)
        {
            Seen.Add(text);
            return Task.FromResult(new float[] { 1, 0, 0, 0 });
        }
    }

    [Fact]
    public async Task EmbedBatch_embeds_each_rows_source_text()
    {
        var provider = new SpyProvider();
        var rows = new (object, string)[] { (1, "alpha"), (2, "beta") };
        var result = await ReindexJobProcessor.EmbedBatchAsync(rows, provider, default);

        provider.Seen.Should().Equal("alpha", "beta");
        result.Should().HaveCount(2);
        result[0].Vector.Should().Equal(1f, 0f, 0f, 0f);
    }
}
