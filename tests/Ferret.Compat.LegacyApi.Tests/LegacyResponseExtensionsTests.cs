using System.Text.Json;
using Ferret.Abstractions;
using Ferret.Compat.LegacyApi;
using FluentAssertions;
using Xunit;

namespace Ferret.Compat.LegacyApi.Tests;

public class LegacyResponseExtensionsTests
{
    [Fact]
    public void ToLegacyResponse_renames_fields()
    {
        var src = new OffsetResult<TestItem>
        {
            Items = [new TestItem { Id = Guid.NewGuid(), Name = "a" }, new TestItem { Id = Guid.NewGuid(), Name = "b" }],
            Page = 1,
            Limit = 25,
            TotalCount = 200,
            HasMore = true,
            HasPrev = true,
        };
        var compat = src.ToLegacyResponse(i => i.Id);
        compat.Items.Should().HaveCount(2);
        compat.Page.Should().Be(1);
        compat.Count.Should().Be(25);
        compat.Total.Should().Be(200);
        compat.MatchInfo.Should().BeNull();
    }

    [Fact]
    public void Json_serialization_uses_legacy_property_names()
    {
        var src = new OffsetResult<TestItem>
        {
            Items = [new TestItem { Id = Guid.Empty, Name = "x" }],
            Page = 0,
            Limit = 25,
            TotalCount = 1,
        };
        var compat = src.ToLegacyResponse(i => i.Id);
        var json = JsonSerializer.Serialize(compat);
        json.Should().Contain("\"items\"");
        json.Should().Contain("\"page\"");
        json.Should().Contain("\"count\"");
        json.Should().Contain("\"total\"");
        json.Should().Contain("\"match_info\"");
        json.Should().NotContain("\"totalCount\"");
        json.Should().NotContain("\"pageSize\"");
    }

    public sealed record TestItem { public Guid Id { get; init; } public string Name { get; init; } = ""; }
}
