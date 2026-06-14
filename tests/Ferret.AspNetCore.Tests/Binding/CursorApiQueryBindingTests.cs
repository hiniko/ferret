using Ferret.Abstractions;
using Ferret.AspNetCore;
using FluentAssertions;
using Xunit;

namespace Ferret.AspNetCore.Tests.Binding;

public class CursorApiQueryBindingTests
{
    [Fact]
    public void ToPagedQuery_sets_cursor_mode_forward_when_after()
    {
        var q = new CursorApiQuery { After = "tok" };
        var pq = q.ToPagedQuery<Widget, Guid>(new PaginationDefaults(25, 100));
        pq.Mode.Should().Be(PaginationMode.Cursor);
        pq.CursorDirection.Should().Be(CursorDirection.Forward);
        pq.Cursor.Should().Be("tok");
    }

    [Fact]
    public void ToPagedQuery_sets_cursor_mode_backward_when_before()
    {
        var q = new CursorApiQuery { Before = "tok" };
        var pq = q.ToPagedQuery<Widget, Guid>(new PaginationDefaults(25, 100));
        pq.CursorDirection.Should().Be(CursorDirection.Backward);
        pq.Cursor.Should().Be("tok");
    }

    [Fact]
    public void ToPagedQuery_first_page_when_neither_after_nor_before()
    {
        var q = new CursorApiQuery();
        var pq = q.ToPagedQuery<Widget, Guid>(new PaginationDefaults(25, 100));
        pq.Mode.Should().Be(PaginationMode.Cursor);
        pq.CursorDirection.Should().Be(CursorDirection.None);
        pq.Cursor.Should().BeNull();
    }

    [Fact]
    public void ToPagedQuery_throws_when_both_after_and_before()
    {
        var q = new CursorApiQuery { After = "a", Before = "b" };
        Action act = () => q.ToPagedQuery<Widget, Guid>(new PaginationDefaults(25, 100));
        act.Should().Throw<InvalidOperationException>().WithMessage("*one of after, before*");
    }

    [Fact]
    public void ToPagedQuery_throws_when_limit_exceeds_max()
    {
        var q = new CursorApiQuery { Limit = 500 };
        Action act = () => q.ToPagedQuery<Widget, Guid>(new PaginationDefaults(25, 100));
        act.Should().Throw<InvalidOperationException>().WithMessage("*100*");
    }

    private sealed class Widget : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }
    }
}
