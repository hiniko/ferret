using Ferret.Abstractions;
using Ferret.Abstractions.Search;
using FluentAssertions;
using Xunit;

namespace Ferret.Core.Tests.Engine;

public sealed class EntityModelBuilderFullTextGroupsTests
{
    [SearchableEntity]
    [SearchGroup("content", FullTextConfig = "english")]
    private sealed class Doc
    {
        public Guid Id { get; init; }

        [Searchable(Backend = SearchBackend.FullText, Group = "content", Weight = 2.0f)]
        public string Title { get; init; } = "";

        [Searchable(Backend = SearchBackend.FullText, Group = "content", Weight = 1.0f)]
        public string Body { get; init; } = "";

        [Searchable(Backend = SearchBackend.FullText, Group = "tags", Weight = 1.0f)]
        public string Tags { get; init; } = "";
    }

    [Fact]
    public void Resolves_groups_with_overrides_and_defaults()
    {
        var model = EntityModelBuilder.Build(typeof(Doc), new SnakeCaseNamingStrategy());

        model.FullTextGroups.Should().HaveCount(2);

        var content = model.FullTextGroups.Single(g => g.Name == "content");
        content.FullTextConfig.Should().Be("english");
        content.Properties.Should().HaveCount(2);
        content.Properties.Single(p => p.PropertyName == "Title").Weight.Should().Be(FullTextWeightBucket.A);
        content.Properties.Single(p => p.PropertyName == "Body").Weight.Should().Be(FullTextWeightBucket.B);

        var tags = model.FullTextGroups.Single(g => g.Name == "tags");
        tags.FullTextConfig.Should().Be("simple");                              // builder default
        tags.Reindex.Should().Be(ReindexMode.Inline);                           // builder default
    }

    [Fact]
    public void Conflicting_FullTextConfig_within_a_group_throws()
    {
        Action act = () => EntityModelBuilder.Build(typeof(Conflicted), new SnakeCaseNamingStrategy());
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*conflicting FullTextConfig*");
    }

    [SearchableEntity]
    [SearchGroup("content", FullTextConfig = "english")]
    private sealed class Article : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }

        [Searchable(Backend = SearchBackend.FullText, Group = "content", Weight = 2.0f)]
        public string Headline { get; init; } = "";

        [SearchJoin]
        public Author? Author { get; init; }
    }

    [SearchableEntity]
    private sealed class Author : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }

        [Searchable(Backend = SearchBackend.FullText, Group = "content", Weight = 1.0f)]
        public string Name { get; init; } = "";
    }

    [Fact]
    public void Group_mixing_owner_local_and_joined_searchables_carries_join_path()
    {
        var model = EntityModelBuilder.Build(typeof(Article), new SnakeCaseNamingStrategy());

        var content = model.FullTextGroups.Single(g => g.Name == "content");
        content.Properties.Should().HaveCount(2);

        var headline = content.Properties.Single(p => p.PropertyName == "Headline");
        headline.Join.Should().BeNull();

        var name = content.Properties.Single(p => p.PropertyName == "Name");
        name.Join.Should().NotBeNull();
        name.Join!.IsDirect.Should().BeFalse();
        name.Join.Hops.Should().ContainSingle()
            .Which.EntityType.Should().Be(typeof(Author));
    }

    [SearchableEntity]
    private sealed class Conflicted
    {
        public Guid Id { get; init; }

        [Searchable(Backend = SearchBackend.FullText, Group = "g", FullTextConfig = "english")]
        public string A { get; init; } = "";

        [Searchable(Backend = SearchBackend.FullText, Group = "g", FullTextConfig = "simple")]
        public string B { get; init; } = "";
    }
}
