using Ferret.Core.Engine.Reindex;
using FluentAssertions;
using Xunit;

namespace Ferret.Core.Tests.Engine.Reindex;

public sealed class ReindexCompositeKeyCodecTests
{
    [Fact]
    public void Round_trips_first_part_containing_separator_and_escape()
    {
        var original = new object[] { @"a|b\c", "42" };

        var encoded = ReindexJobProcessor.EncodeCompositeKey(original);
        var decoded = ReindexJobProcessor.DecodeCompositeKey(encoded, original.Length);

        decoded.Should().Equal(@"a|b\c", "42");
    }

    [Fact]
    public void Round_trips_plain_parts()
    {
        var original = new object[] { "tenant-1", "100" };

        var encoded = ReindexJobProcessor.EncodeCompositeKey(original);
        var decoded = ReindexJobProcessor.DecodeCompositeKey(encoded, original.Length);

        decoded.Should().Equal("tenant-1", "100");
    }

    [Fact]
    public void Round_trips_separators_in_multiple_parts()
    {
        var original = new object[] { "a|b", "c|d", @"e\|f" };

        var encoded = ReindexJobProcessor.EncodeCompositeKey(original);
        var decoded = ReindexJobProcessor.DecodeCompositeKey(encoded, original.Length);

        decoded.Should().Equal("a|b", "c|d", @"e\|f");
    }
}
