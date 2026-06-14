using Ferret.Core.Engine;
using FluentAssertions;
using Xunit;

namespace Ferret.Core.Tests;

public class PackageDependenciesTests
{
    [Fact]
    public void Core_assembly_does_not_reference_aspnetcore()
    {
        var refs = typeof(IFerretEngine).Assembly
            .GetReferencedAssemblies()
            .Select(n => n.Name);
        refs.Should().NotContain(n => n!.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
    }
}
