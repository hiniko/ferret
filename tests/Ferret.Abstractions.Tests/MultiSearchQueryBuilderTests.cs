using FluentAssertions;
using Xunit;

namespace Ferret.Abstractions.Tests;

public class MultiSearchQueryBuilderTests
{
    private sealed class FakeEntity
    {
        public string Name { get; set; } = "";
    }

    private static MultiSearchQueryBuilder Valid() =>
        new MultiSearchQueryBuilder().Search("term")
            .Entity<FakeEntity>(e => new FakeEntity { Name = e.Name });

    [Fact]
    public void build_produces_query_with_defaults()
    {
        var q = Valid().Build();
        q.Search.Should().Be("term");
        q.PerEntityLimit.Should().Be(5);
        q.Types.Should().BeNull();
        q.Entities.Should().ContainSingle()
            .Which.EntityType.Should().Be(typeof(FakeEntity));
    }

    [Fact]
    public void blank_search_throws() =>
        FluentActions.Invoking(() => new MultiSearchQueryBuilder()
                .Search("  ")
                .Entity<FakeEntity>(e => new FakeEntity { Name = e.Name })
                .Build())
            .Should().Throw<InvalidOperationException>().WithMessage("*search*");

    [Fact]
    public void no_entities_throws() =>
        FluentActions.Invoking(() => new MultiSearchQueryBuilder().Search("x").Build())
            .Should().Throw<InvalidOperationException>().WithMessage("*entity*");

    [Fact]
    public void duplicate_entity_throws() =>
        FluentActions.Invoking(() => Valid()
                .Entity<FakeEntity>(e => new FakeEntity { Name = e.Name })
                .Build())
            .Should().Throw<InvalidOperationException>().WithMessage("*FakeEntity*");

    [Fact]
    public void nonpositive_limit_throws() =>
        FluentActions.Invoking(() => Valid().PerEntityLimit(0).Build())
            .Should().Throw<ArgumentException>();

    [Fact]
    public void filters_flow_to_definition()
    {
        var q = new MultiSearchQueryBuilder().Search("x")
            .Entity<FakeEntity>(e => new FakeEntity { Name = e.Name },
                o => o.Filter(new FilterClause { Field = "Name", Operator = FilterOperator.Equals, Value = "a" }))
            .Build();
        q.Entities[0].Filters.Should().ContainSingle().Which.Field.Should().Be("Name");
    }

    [Fact]
    public void types_flow_to_query()
    {
        var q = Valid().Types("fake_entities").Build();
        q.Types.Should().ContainSingle().Which.Should().Be("fake_entities");
    }
}
