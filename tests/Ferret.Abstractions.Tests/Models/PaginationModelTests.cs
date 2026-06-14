using Ferret.Abstractions;
using FluentAssertions;
using Xunit;

namespace Ferret.Abstractions.Tests.Models;

public class PaginationModelTests
{
    private sealed class Widget : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }
    }

    [Fact]
    public void PagedQuery_defaults_to_offset_mode_with_no_cursor()
    {
        var q = new PagedQuery<Widget, Guid>();

        q.Mode.Should().Be(PaginationMode.Offset);
        q.CursorDirection.Should().Be(CursorDirection.None);
        q.Cursor.Should().BeNull();
        q.Limit.Should().Be(25);
        q.Page.Should().BeNull();
        q.RequestTotalCount.Should().BeFalse();
    }

    [Fact]
    public void PagedQuery_cursor_mode_round_trips()
    {
        var q = new PagedQuery<Widget, Guid>
        {
            Mode = PaginationMode.Cursor,
            Cursor = "abc",
            CursorDirection = CursorDirection.Forward,
            Limit = 50,
        };

        q.Mode.Should().Be(PaginationMode.Cursor);
        q.Cursor.Should().Be("abc");
        q.CursorDirection.Should().Be(CursorDirection.Forward);
        q.Limit.Should().Be(50);
    }

    [Fact]
    public void OffsetResult_round_trips()
    {
        var r = new OffsetResult<string>
        {
            Items = ["a", "b"],
            Limit = 25,
            Page = 0,
            TotalCount = 2,
            HasMore = false,
            HasPrev = false,
        };
        r.Items.Should().Equal("a", "b");
        r.TotalCount.Should().Be(2);
    }

    [Fact]
    public void CursorResult_round_trips()
    {
        var r = new CursorResult<string>
        {
            Items = ["x"],
            Limit = 25,
            NextCursor = "next",
            PrevCursor = null,
            HasMore = true,
            HasPrev = false,
        };
        r.NextCursor.Should().Be("next");
        r.HasMore.Should().BeTrue();
    }
}
