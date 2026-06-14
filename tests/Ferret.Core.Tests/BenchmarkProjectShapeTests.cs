using FluentAssertions;
using Xunit;

namespace Ferret.Core.Tests;

public class BenchmarkProjectShapeTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ferret.slnx")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("the test must be able to locate the repository root (Ferret.slnx)");
        return dir!.FullName;
    }

    [Fact]
    public void Benchmarks_project_is_in_solution_and_references_core()
    {
        var root = RepoRoot();

        var csprojPath = Path.Combine(root, "benchmarks", "Ferret.Benchmarks", "Ferret.Benchmarks.csproj");
        File.Exists(csprojPath).Should().BeTrue("the benchmark project file must exist");

        var csproj = File.ReadAllText(csprojPath);
        csproj.Should().Contain("Ferret.Core/Ferret.Core.csproj");
        csproj.Should().Contain("Ferret.Hydration.Dapper/Ferret.Hydration.Dapper.csproj");
        csproj.Should().Contain("Testcontainers.PostgreSql");

        var slnx = File.ReadAllText(Path.Combine(root, "Ferret.slnx"));
        slnx.Should().Contain("benchmarks/Ferret.Benchmarks/Ferret.Benchmarks.csproj");
    }
}
