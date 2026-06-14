using FluentAssertions;
using Xunit;

namespace Ferret.Core.Tests.Sql;

public class PostgresDialectTests
{
    private readonly PostgresDialect _d = new();

    [Theory]
    [InlineData("foo", "\"foo\"")]
    [InlineData("Foo Bar", "\"Foo Bar\"")]
    [InlineData("a\"b", "\"a\"\"b\"")]
    public void QuoteIdentifier_doubles_internal_quotes(string input, string expected)
        => _d.QuoteIdentifier(input).Should().Be(expected);

    [Fact]
    public void PagingClause_emits_limit_offset() =>
        _d.PagingClause(25, 50).Should().Be("LIMIT 25 OFFSET 50");

    [Fact]
    public void CountOverWindow_uses_window_function() =>
        _d.CountOverWindow().Should().Be("COUNT(*) OVER()");

    [Fact]
    public void ArrayParameter_returns_any_form() =>
        _d.ArrayParameter("@ids").Should().Be("ANY(@ids)");
}
