using Ferret.Abstractions;
using Ferret.Abstractions.Models;
using Ferret.AspNetCore.Binding;
using FluentAssertions;
using Xunit;

namespace Ferret.AspNetCore.Tests.Binding;

public class ClauseParsingTests
{
    [Fact]
    public void ParseFilters_parses_compact_clause()
    {
        var result = ClauseParsing.ParseFilters(new[] { "name:eq:alpha" });

        result.Should().ContainSingle();
        result[0].Should().BeEquivalentTo(new FilterClause { Field = "name", Operator = FilterOperator.Equals, Value = "alpha" });
    }

    [Theory]
    [InlineData("eq", FilterOperator.Equals)]
    [InlineData("neq", FilterOperator.NotEquals)]
    [InlineData("contains", FilterOperator.Contains)]
    [InlineData("gt", FilterOperator.GreaterThan)]
    [InlineData("gte", FilterOperator.GreaterThanOrEqual)]
    [InlineData("lt", FilterOperator.LessThan)]
    [InlineData("lte", FilterOperator.LessThanOrEqual)]
    [InlineData("in", FilterOperator.In)]
    [InlineData("isnull", FilterOperator.IsNull)]
    [InlineData("notnull", FilterOperator.NotNull)]
    public void ParseFilters_supports_all_operators(string wire, FilterOperator expected)
    {
        var result = ClauseParsing.ParseFilters(new[] { $"field:{wire}:val" });

        result.Should().ContainSingle().Which.Operator.Should().Be(expected);
    }

    [Theory]
    [InlineData("deletedAt:isnull", FilterOperator.IsNull)]
    [InlineData("deletedAt:notnull", FilterOperator.NotNull)]
    [InlineData("deletedAt:isnull:", FilterOperator.IsNull)]
    public void ParseFilters_valueless_operators_accept_two_part_form(string wire, FilterOperator expected)
    {
        var result = ClauseParsing.ParseFilters(new[] { wire });

        result.Should().ContainSingle();
        result[0].Operator.Should().Be(expected);
        result[0].Value.Should().BeEmpty();
    }

    [Fact]
    public void ParseFilters_operator_is_case_insensitive()
    {
        var result = ClauseParsing.ParseFilters(new[] { "field:GTE:5" });

        result.Should().ContainSingle().Which.Operator.Should().Be(FilterOperator.GreaterThanOrEqual);
    }

    [Fact]
    public void ParseFilters_skips_unknown_operator()
    {
        var result = ClauseParsing.ParseFilters(new[] { "name:weird:alpha" });

        result.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseFilters_skips_blank(string? raw)
    {
        var result = ClauseParsing.ParseFilters(new[] { raw });

        result.Should().BeEmpty();
    }

    [Theory]
    [InlineData("name")]
    [InlineData("name:eq")]
    public void ParseFilters_skips_malformed(string raw)
    {
        var result = ClauseParsing.ParseFilters(new[] { raw });

        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseFilters_keeps_colons_in_value()
    {
        var result = ClauseParsing.ParseFilters(new[] { "ts:eq:12:30:00" });

        result.Should().ContainSingle().Which.Value.Should().Be("12:30:00");
    }

    [Fact]
    public void ParseSorts_defaults_to_ascending()
    {
        var result = ClauseParsing.ParseSorts(new[] { "name" });

        result.Should().ContainSingle();
        result[0].Should().BeEquivalentTo(new SortClause { Field = "name", Direction = SortDirection.Ascending });
    }

    [Fact]
    public void ParseSorts_parses_descending()
    {
        var result = ClauseParsing.ParseSorts(new[] { "created_at:desc" });

        result.Should().ContainSingle().Which.Direction.Should().Be(SortDirection.Descending);
    }

    [Fact]
    public void ParseSorts_direction_is_case_insensitive()
    {
        var result = ClauseParsing.ParseSorts(new[] { "created_at:DESC" });

        result.Should().ContainSingle().Which.Direction.Should().Be(SortDirection.Descending);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseSorts_skips_blank(string? raw)
    {
        var result = ClauseParsing.ParseSorts(new[] { raw });

        result.Should().BeEmpty();
    }
}
