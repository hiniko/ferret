using Ferret.Abstractions;
using Ferret.Compat.LegacyApi;
using FluentAssertions;
using Xunit;

namespace Ferret.Compat.LegacyApi.Tests;

public class LegacyApiQueryTests
{
    [Fact]
    public void ToPagedQuery_maps_page_size_to_limit()
    {
        var q = new LegacyApiQuery { Page = 0, PageSize = 50 };
        var pq = q.ToPagedQuery<Widget, Guid>(new PaginationDefaults(25, 100));
        pq.Mode.Should().Be(PaginationMode.Offset);
        pq.Limit.Should().Be(50);
        pq.Page.Should().Be(0);
        pq.RequestTotalCount.Should().BeTrue();
    }

    [Fact]
    public void ToPagedQuery_uses_default_when_page_size_null()
    {
        var q = new LegacyApiQuery();
        var pq = q.ToPagedQuery<Widget, Guid>(new PaginationDefaults(25, 100));
        pq.Limit.Should().Be(25);
    }

    [Fact]
    public void ToPagedQuery_forwards_search_and_filter()
    {
        var q = new LegacyApiQuery
        {
            Search = "widge",
            IncludeMatchInfo = true,
            SearchFields = ["Name"],
            Filter = [new FilterClause { Field = "Category", Operator = FilterOperator.Equals, Value = "tools" }],
        };
        var pq = q.ToPagedQuery<Widget, Guid>(new PaginationDefaults(25, 100));
        pq.Search.Should().Be("widge");
        pq.IncludeMatchInfo.Should().BeTrue();
        pq.SearchFields.Should().Equal("Name");
        pq.Filter.Should().HaveCount(1);
    }

    [Fact]
    public void ToPagedQuery_throws_when_page_size_exceeds_max()
    {
        var q = new LegacyApiQuery { PageSize = 500 };
        Action act = () => q.ToPagedQuery<Widget, Guid>(new PaginationDefaults(25, 100));
        act.Should().Throw<InvalidOperationException>().WithMessage("*100*");
    }

    private sealed class Widget : ISearchableEntity<Guid> { public Guid Id { get; init; } }
}
