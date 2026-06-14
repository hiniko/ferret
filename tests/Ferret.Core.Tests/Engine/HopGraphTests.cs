using Ferret.Benchmarks.Model;
using FluentAssertions;
using Xunit;

namespace Ferret.Core.Tests.Engine;

public sealed class HopGraphTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void EntityModel_for_depth_N_has_searchable_at_hop_N(int depth)
    {
        var ownerType = HopGraph.EntityTypeForDepth(depth);

        var model = EntityModelBuilder.Build(ownerType, new SnakeCaseNamingStrategy());

        var leaf = model.SearchableProperties
            .OrderByDescending(s => s.JoinPath.Hops.Count)
            .First();

        leaf.JoinPath.Hops.Count.Should().Be(depth);
    }
}
