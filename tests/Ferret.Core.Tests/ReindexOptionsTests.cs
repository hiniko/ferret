using Ferret.Core.Backends.FullText;
using Ferret.Core.Engine;
using FluentAssertions;
using Xunit;

namespace Ferret.Core.Tests;

public sealed class ReindexOptionsTests
{
    [Fact]
    public void Null_batch_size_and_delay_fall_back_to_FullTextOptions()
    {
        var fullText = new FullTextOptions();
        var options = new ReindexOptions { BatchSize = null, BatchDelay = null };

        var (batchSize, batchDelay) = options.Resolve(fullText);

        batchSize.Should().Be(5000);
        batchSize.Should().Be(fullText.ConcurrentBatchSize);
        batchDelay.Should().Be(TimeSpan.Zero);
        batchDelay.Should().Be(fullText.ConcurrentBatchDelay);
    }
}
