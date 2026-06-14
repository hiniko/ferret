using Ferret.Abstractions;
using Ferret.Abstractions.Attributes;
using Ferret.Core.Engine;
using FluentAssertions;
using Xunit;

namespace Ferret.Core.Tests.Engine;

public class ResolveVectorGroupsTests
{
    [SearchableEntity(Table = "vdocs")]
    private sealed class VDoc : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }
        [Searchable(Backend = SearchBackend.Vector, Group = "content", EmbeddingDimensions = 8)]
        public string Body { get; init; } = "";
    }

    [SearchableEntity(Table = "cdocs")]
    private sealed class ConflictDoc : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }
        [Searchable(Backend = SearchBackend.Vector, Group = "content", EmbeddingDimensions = 8)]
        public string A { get; init; } = "";
        [Searchable(Backend = SearchBackend.Vector, Group = "content", EmbeddingDimensions = 16)]
        public string B { get; init; } = "";
    }

    [Fact]
    public void Resolves_one_vector_group_with_dimensions()
    {
        var model = EntityModelBuilder.Build(typeof(VDoc), new SnakeCaseNamingStrategy());
        model.VectorGroups.Should().ContainSingle();
        var g = model.VectorGroups[0];
        g.Name.Should().Be("content");
        g.Dimensions.Should().Be(8);
        g.Properties.Should().ContainSingle().Which.EmbeddingSource.Should().Be("Body");
    }

    [Fact]
    public void Conflicting_dimensions_in_one_group_throw()
    {
        var act = () => EntityModelBuilder.Build(typeof(ConflictDoc), new SnakeCaseNamingStrategy());
        act.Should().Throw<InvalidOperationException>().WithMessage("*conflicting*dimension*");
    }
}
