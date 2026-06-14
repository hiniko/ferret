using Ferret.Core.Engine;
using Ferret.Core.Engine.Cursor;
using FluentAssertions;
using Xunit;

namespace Ferret.Core.Tests.Engine;

public class SearchCursorWrapTests
{
    [Fact]
    public void Next_cursor_null_past_cap()
    {
        var (next, more) = FerretEngine.ComputeNextSearchCursor(offset: 190, limit: 20, fetchedCount: 20, maxOffset: 200, fingerprint: "f");
        next.Should().BeNull();
        more.Should().BeFalse();
    }

    [Fact]
    public void Next_cursor_advances_within_cap()
    {
        var (next, more) = FerretEngine.ComputeNextSearchCursor(offset: 0, limit: 20, fetchedCount: 20, maxOffset: 200, fingerprint: "f");
        more.Should().BeTrue();
        CursorToken.Decode(next!).Offset.Should().Be(20);
    }

    [Fact]
    public void Next_cursor_null_when_partial_page()
    {
        var (next, more) = FerretEngine.ComputeNextSearchCursor(offset: 0, limit: 20, fetchedCount: 7, maxOffset: 200, fingerprint: "f");
        next.Should().BeNull();
        more.Should().BeFalse();
    }
}
