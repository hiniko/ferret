using FluentAssertions;
using Xunit;

namespace Ferret.Core.Tests;

public sealed class EntityModelBuilderPreviousGroupTests
{
    [SearchableEntity]
    private sealed class Doc
    {
        public Guid Id { get; init; }

        [Searchable(Backend = SearchBackend.FullText, Group = "body", PreviousGroup = "content")]
        public string Body { get; init; } = "";

        [Searchable(Backend = SearchBackend.FullText, Group = "body")]
        public string Title { get; init; } = "";
    }

    [Fact]
    public void PreviousGroup_flows_from_attribute_to_SearchablePropertyInfo()
    {
        var model = EntityModelBuilder.Build(typeof(Doc), new SnakeCaseNamingStrategy());

        var body = model.SearchableProperties.Single(s => s.Property.Name == "Body");
        body.PreviousGroup.Should().Be("content");

        var title = model.SearchableProperties.Single(s => s.Property.Name == "Title");
        title.PreviousGroup.Should().BeNull();
    }

    [SearchableEntity]
    private sealed class ConflictingDoc
    {
        public Guid Id { get; init; }

        [Searchable(Backend = SearchBackend.FullText, Group = "body", PreviousGroup = "content")]
        public string Body { get; init; } = "";

        [Searchable(Backend = SearchBackend.FullText, Group = "body", PreviousGroup = "article")]
        public string Title { get; init; } = "";
    }

    [Fact]
    public void Conflicting_PreviousGroup_within_group_throws()
    {
        var act = () => EntityModelBuilder.Build(typeof(ConflictingDoc), new SnakeCaseNamingStrategy());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*conflicting PreviousGroup values*");
    }

    [SearchableEntity]
    private sealed class SelfRenameDoc
    {
        public Guid Id { get; init; }

        [Searchable(Backend = SearchBackend.FullText, Group = "body", PreviousGroup = "body")]
        public string Body { get; init; } = "";
    }

    [Fact]
    public void SelfRename_throws()
    {
        var act = () => EntityModelBuilder.Build(typeof(SelfRenameDoc), new SnakeCaseNamingStrategy());

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void FullTextGroupRenames_maps_renamed_group()
    {
        var model = EntityModelBuilder.Build(typeof(Doc), new SnakeCaseNamingStrategy());

        model.FullTextGroupRenames.Should().ContainKey("body");
        model.FullTextGroupRenames["body"].Should().Be("content");
    }

    [Fact]
    public void FullTextGroup_domain_does_not_expose_hint()
    {
        typeof(Ferret.Abstractions.Search.FullTextGroup)
            .GetProperty("PreviousGroup")
            .Should().BeNull();
    }
}
