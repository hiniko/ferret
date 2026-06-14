using FluentAssertions;
using Xunit;

namespace Ferret.Migrations.Tests;

public class SmokeTest
{
    [Fact]
    public void Assembly_loads()
    {
        var name = typeof(Ferret.Migrations.Annotations.SearchableAnnotationKeys).Assembly.GetName().Name;
        name.Should().Be("Ferret.Migrations");
    }
}
