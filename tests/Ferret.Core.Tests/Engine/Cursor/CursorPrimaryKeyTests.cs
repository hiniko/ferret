using FluentAssertions;
using Xunit;

namespace Ferret.Core.Tests.Engine.Cursor;

public class CursorPrimaryKeyTests
{
    [Fact]
    public void Encode_decode_guid_round_trip()
    {
        var id = Guid.NewGuid();
        var s = CursorPrimaryKey.Encode<Guid>(id);
        var back = CursorPrimaryKey.Decode<Guid>(s);
        back.Should().Be(id);
    }

    [Fact]
    public void Encode_decode_long_round_trip()
    {
        var s = CursorPrimaryKey.Encode<long>(12345L);
        CursorPrimaryKey.Decode<long>(s).Should().Be(12345L);
    }

    [Fact]
    public void Encode_decode_int_round_trip()
    {
        var s = CursorPrimaryKey.Encode<int>(42);
        CursorPrimaryKey.Decode<int>(s).Should().Be(42);
    }

    [Fact]
    public void Encode_decode_string_round_trip()
    {
        var s = CursorPrimaryKey.Encode<string>("sku-001");
        CursorPrimaryKey.Decode<string>(s).Should().Be("sku-001");
    }

    [Fact]
    public void Decode_throws_on_unsupported_type()
    {
        Action act = () => CursorPrimaryKey.Decode<DateTime>("anything");
        act.Should().Throw<NotSupportedException>().WithMessage("*DateTime*");
    }

    [Fact]
    public void Encode_throws_on_unsupported_type()
    {
        Action act = () => CursorPrimaryKey.Encode<DateTime>(DateTime.UtcNow);
        act.Should().Throw<NotSupportedException>().WithMessage("*DateTime*");
    }

    [Fact]
    public void Decode_malformed_guid_throws_FormatException()
    {
        Action act = () => CursorPrimaryKey.Decode<Guid>("not-a-guid");
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Decode_malformed_long_throws_FormatException()
    {
        Action act = () => CursorPrimaryKey.Decode<long>("abc");
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void MultiPart_guid_long_round_trip()
    {
        var id = Guid.NewGuid();
        var parts = new (object value, Type type)[] { (id, typeof(Guid)), (12345L, typeof(long)) };
        var encoded = CursorPrimaryKey.Encode(parts);
        var decoded = CursorPrimaryKey.Decode(encoded, new[] { typeof(Guid), typeof(long) });
        decoded.Should().HaveCount(2);
        decoded[0].Should().Be(id);
        decoded[1].Should().Be(12345L);
    }

    [Fact]
    public void Decode_arity_mismatch_throws()
    {
        var encoded = new[] { "a", "b" };
        Action act = () => CursorPrimaryKey.Decode(encoded, new[] { typeof(string) });
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void MultiPart_unsupported_type_throws()
    {
        var parts = new (object value, Type type)[] { (DateTime.UtcNow, typeof(DateTime)) };
        Action act = () => CursorPrimaryKey.Encode(parts);
        act.Should().Throw<NotSupportedException>();
    }
}
