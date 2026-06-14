using Ferret.Abstractions;
using Ferret.AspNetCore;
using FluentAssertions;
using Xunit;

namespace Ferret.AspNetCore.Tests.Binding;

public class OffsetApiQueryBindingTests
{
    [Fact]
    public void ToPagedQuery_sets_offset_mode_and_applies_default_limit()
    {
        var q = new OffsetApiQuery { Page = 0 };
        var d = new PaginationDefaults(25, 100);
        var pq = q.ToPagedQuery<Widget, Guid>(d);
        pq.Mode.Should().Be(PaginationMode.Offset);
        pq.Limit.Should().Be(25);
        pq.Page.Should().Be(0);
        pq.RequestTotalCount.Should().BeTrue();
    }

    [Fact]
    public void ToPagedQuery_uses_supplied_limit()
    {
        var q = new OffsetApiQuery { Limit = 50 };
        var pq = q.ToPagedQuery<Widget, Guid>(new PaginationDefaults(25, 100));
        pq.Limit.Should().Be(50);
    }

    [Fact]
    public void ToPagedQuery_throws_when_limit_exceeds_max()
    {
        var q = new OffsetApiQuery { Limit = 500 };
        var d = new PaginationDefaults(25, 100);
        Action act = () => q.ToPagedQuery<Widget, Guid>(d);
        act.Should().Throw<InvalidOperationException>().WithMessage("*100*");
    }

    [Fact]
    public void ToPagedQuery_forwards_q_fields_filter_sort()
    {
        var q = new OffsetApiQuery
        {
            Q = "widge",
            Fields = ["Name"],
            MatchInfo = true,
            Sort = [new SortClause { Field = "Name", Direction = SortDirection.Ascending }],
            Filter = [new FilterClause { Field = "Price", Operator = FilterOperator.LessThan, Value = "50" }],
        };
        var pq = q.ToPagedQuery<Widget, Guid>(new PaginationDefaults(25, 100));
        pq.Search.Should().Be("widge");
        pq.SearchFields.Should().Equal("Name");
        pq.IncludeMatchInfo.Should().BeTrue();
        pq.Sort.Should().HaveCount(1);
        pq.Filter.Should().HaveCount(1);
    }

    private sealed class Widget : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }
    }
}
