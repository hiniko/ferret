using System;
using System.Text.RegularExpressions;
using Ferret.Benchmarks.Infrastructure;
using FluentAssertions;
using Xunit;

namespace Ferret.Core.Tests.Backends;

public class GeneratedSqlCaptureTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Depth_N_sql_contains_N_inner_joins(int depth)
    {
        var captured = GeneratedSqlCapture.Capture(depth, "needle");

        var innerJoins = Regex.Matches(captured.Sql, "INNER JOIN", RegexOptions.IgnoreCase).Count;
        innerJoins.Should().Be(depth);

        captured.Sql.Should().Contain("GROUP BY");
    }
}
