using Ferret.Core.Engine.Cursor;
using FluentAssertions;
using Xunit;

namespace Ferret.Core.Tests.Engine.Cursor;

public class OffsetCursorTests
{
    [Fact]
    public void Offset_payload_round_trips()
    {
        var token = CursorToken.Encode(new CursorPayload { Version = 2, Offset = 40, Fingerprint = "abc" });
        var back = CursorToken.Decode(token);
        back.Version.Should().Be(2);
        back.Offset.Should().Be(40);
        back.Fingerprint.Should().Be("abc");
    }

    [Fact]
    public void Fingerprint_includes_search_term()
    {
        var a = CursorFingerprint.Compute("t", [], [], ["id"], searchTerm: "cat");
        var b = CursorFingerprint.Compute("t", [], [], ["id"], searchTerm: "dog");
        a.Should().NotBe(b);
    }

    [Fact]
    public void Fingerprint_unchanged_when_no_search_term()
    {
        var withoutArg = CursorFingerprint.Compute("t", [], [], ["id"]);
        var withNull = CursorFingerprint.Compute("t", [], [], ["id"], searchTerm: null);
        withoutArg.Should().Be(withNull);
    }
}
