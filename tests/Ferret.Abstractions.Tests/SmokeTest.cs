using FluentAssertions;
using Xunit;

namespace Ferret.Abstractions.Tests;

public class SmokeTest
{
    [Fact]
    public void Assembly_loads()
    {
        typeof(ISearchableEntity<>).Assembly.GetName().Name
            .Should().Be("Ferret.Abstractions");
    }
}
