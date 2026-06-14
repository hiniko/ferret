using Ferret.Abstractions;
using FluentAssertions;
using Xunit;

namespace Ferret.Abstractions.Tests.Search;

public class AbstractionShapeTests
{
    [Fact]
    public void ISearchBackend_required_members()
    {
        var t = typeof(ISearchBackend);
        t.IsInterface.Should().BeTrue();
        t.GetProperty("Name").Should().NotBeNull();
        t.GetMethod("CanHandle").Should().NotBeNull();
        t.GetMethod("BuildRankingQuery").Should().NotBeNull();
        t.GetMethod("GetIndexDefinition").Should().NotBeNull();
        t.GetMethod("ResolveQueryVectorAsync").Should().NotBeNull();
    }

    [Fact]
    public void IFerretSession_inherits_IAsyncDisposable()
    {
        typeof(IAsyncDisposable).IsAssignableFrom(typeof(IFerretSession)).Should().BeTrue();
    }

    [Fact]
    public void IEntityHydrator_HydrateAsync_is_generic()
    {
        var m = typeof(IEntityHydrator).GetMethods()
            .Single(x => x.Name == "HydrateAsync");
        m.IsGenericMethodDefinition.Should().BeTrue();
    }
}
