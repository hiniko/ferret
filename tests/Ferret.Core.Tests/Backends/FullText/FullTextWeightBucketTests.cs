using Ferret.Abstractions.Search;
using FluentAssertions;
using Xunit;

namespace Ferret.Core.Tests.Backends.FullText;

public sealed class FullTextWeightBucketTests
{
    [Theory]
    [InlineData(5.0f, FullTextWeightBucket.A)]
    [InlineData(2.0f, FullTextWeightBucket.A)]
    [InlineData(1.99f, FullTextWeightBucket.B)]
    [InlineData(1.0f, FullTextWeightBucket.B)]
    [InlineData(0.5f, FullTextWeightBucket.C)]
    [InlineData(0.49f, FullTextWeightBucket.D)]
    [InlineData(0.0f, FullTextWeightBucket.D)]
    public void Bucket_default_thresholds(float weight, FullTextWeightBucket expected)
    {
        FullTextWeightBucketMapper.Bucket(weight, a: 2.0f, b: 1.0f, c: 0.5f).Should().Be(expected);
    }

    [Fact]
    public void Bucket_respects_custom_thresholds()
    {
        FullTextWeightBucketMapper.Bucket(1.5f, a: 3.0f, b: 2.0f, c: 1.0f)
            .Should().Be(FullTextWeightBucket.C);
    }
}
