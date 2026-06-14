using Ferret.Abstractions.Search;
using FluentAssertions;
using Xunit;

namespace Ferret.Core.Tests.Backends.Vector;

public class VectorGroupTests
{
    [Fact]
    public void VectorGroup_carries_dimensions_and_properties()
    {
        var g = new VectorGroup
        {
            Name = "content",
            Dimensions = 1536,
            Properties = [new VectorGroupProperty { PropertyName = "Body", ColumnName = "body", EmbeddingSource = "Body" }],
        };

        g.Name.Should().Be("content");
        g.Dimensions.Should().Be(1536);
        g.Properties.Should().ContainSingle().Which.EmbeddingSource.Should().Be("Body");
    }
}
