using Ferret.Abstractions;
using Ferret.Abstractions.Attributes;
using Ferret.Core.Engine;
using FluentAssertions;
using Xunit;

namespace Ferret.Core.Tests.Engine;

public class ResolveHybridConfigTests
{
    [SearchableEntity(Table = "hdocs")]
    [HybridBackend(SearchBackend.Vector, Weight = 2.0, ConfidenceThreshold = 0.25)]
    private sealed class HDoc : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }
        [Searchable(Backend = SearchBackend.FullText, Group = "content")]
        public string Title { get; init; } = "";
        [Searchable(Backend = SearchBackend.Vector, Group = "content", EmbeddingDimensions = 8)]
        public string Body { get; init; } = "";
    }

    [SearchableEntity(Table = "sdocs")]
    private sealed class SingleDoc : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }
        [Searchable(Backend = SearchBackend.FullText, Group = "content")]
        public string Title { get; init; } = "";
    }

    [Fact]
    public void Multi_backend_entity_gets_hybrid_config()
    {
        var model = EntityModelBuilder.Build(typeof(HDoc), new SnakeCaseNamingStrategy());
        model.HybridConfig.Should().NotBeNull();
        model.HybridConfig!.Backends.Select(b => b.Backend)
            .Should().BeEquivalentTo(new[] { SearchBackend.FullText, SearchBackend.Vector });
        var vec = model.HybridConfig.Backends.Single(b => b.Backend == SearchBackend.Vector);
        vec.Weight.Should().Be(2.0);
        vec.ConfidenceThreshold.Should().Be(0.25);
        var ft = model.HybridConfig.Backends.Single(b => b.Backend == SearchBackend.FullText);
        double.IsNaN(ft.Weight).Should().BeTrue();
        double.IsNaN(ft.ConfidenceThreshold).Should().BeTrue();
    }

    [Fact]
    public void Single_backend_entity_has_null_hybrid_config()
    {
        var model = EntityModelBuilder.Build(typeof(SingleDoc), new SnakeCaseNamingStrategy());
        model.HybridConfig.Should().BeNull();
    }
}
