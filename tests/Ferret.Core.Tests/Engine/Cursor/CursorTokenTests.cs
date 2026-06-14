using FluentAssertions;
using Xunit;

namespace Ferret.Core.Tests.Engine.Cursor;

public class CursorTokenTests
{
    [Fact]
    public void Round_trip_token_with_single_sort_key()
    {
        var payload = new CursorPayload
        {
            Version = 1,
            SortKeys = ["Widget"],
            PrimaryKeys = ["abc-123"],
            Fingerprint = "0123456789abcdef",
        };
        var encoded = CursorToken.Encode(payload);
        encoded.Should().NotBeNullOrWhiteSpace();
        encoded.Should().NotContain("+").And.NotContain("/").And.NotContain("=");

        var decoded = CursorToken.Decode(encoded);
        decoded.Version.Should().Be(1);
        decoded.SortKeys.Should().Equal("Widget");
        decoded.PrimaryKeys.Should().Equal("abc-123");
        decoded.Fingerprint.Should().Be("0123456789abcdef");
    }

    [Fact]
    public void Round_trip_token_with_multiple_sort_keys()
    {
        var payload = new CursorPayload
        {
            Version = 1,
            SortKeys = ["50", "Brass Hammer"],
            PrimaryKeys = ["00000000000000000000000000000001"],
            Fingerprint = "abcdef0123456789",
        };
        var decoded = CursorToken.Decode(CursorToken.Encode(payload));
        decoded.SortKeys.Should().Equal("50", "Brass Hammer");
    }

    [Fact]
    public void Encode_decode_round_trips_pks_list()
    {
        var payload = new CursorPayload
        {
            Version = 1,
            SortKeys = ["Widget"],
            PrimaryKeys = ["tenant-7", "abc-123"],
            Fingerprint = "0123456789abcdef",
        };
        var decoded = CursorToken.Decode(CursorToken.Encode(payload));
        decoded.PrimaryKeys.Should().Equal("tenant-7", "abc-123");
    }

    [Fact]
    public void Token_json_uses_pks_property_name()
    {
        var payload = new CursorPayload
        {
            Version = 1,
            SortKeys = ["Widget"],
            PrimaryKeys = ["tenant-7", "abc-123"],
            Fingerprint = "0123456789abcdef",
        };
        var decoded = CursorToken.Decode(CursorToken.Encode(payload));
        decoded.PrimaryKeys.Should().HaveCount(2);
        decoded.PrimaryKeys.Should().Equal("tenant-7", "abc-123");
    }

    [Fact]
    public void Decode_garbage_throws()
    {
        Action act = () => CursorToken.Decode("not-a-valid-token-***");
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Decode_truncated_throws()
    {
        var payload = new CursorPayload
        {
            Version = 1,
            SortKeys = [],
            PrimaryKeys = ["x"],
            Fingerprint = "abcdef0123456789",
        };
        var encoded = CursorToken.Encode(payload);
        Action act = () => CursorToken.Decode(encoded.Substring(0, encoded.Length - 5));
        act.Should().Throw<FormatException>();
    }
}
