using FluentAssertions;
using Xunit;

namespace Ferret.Hydration.Dapper.Tests;

public class SmokeTests
{
    [Fact]
    public async Task DapperSession_disposes_cleanly_without_opening_connection()
    {
        await using var session = new DapperSession(
            _ => throw new InvalidOperationException("connection factory should not be called"),
            new PostgresDialect());

        // No call to OpenConnectionAsync — factory must not run.
        Func<Task> act = async () => await session.DisposeAsync();
        await act.Should().NotThrowAsync();
    }
}
