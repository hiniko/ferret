using FluentAssertions;
using Xunit;

namespace Ferret.Core.Tests;

public class DocsShapeTests
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
    public void Sample_document_fixture_has_expected_sections()
    {
        var docPath = Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "sample-document.md");

        File.Exists(docPath).Should().BeTrue("the sample document fixture must exist");

        var doc = File.ReadAllText(docPath);

        doc.Should().Contain("## Down the Rabbit-Hole");
        doc.Should().Contain("## The Pool of Tears");
        doc.Should().Contain("## A Caucus-Race");
        doc.Should().Contain("## Advice from a Caterpillar");

        doc.Should().Contain("Alice");
        doc.Should().Contain("Caterpillar");
    }

    [Fact]
    public void Readme_documents_benchmark_invocation()
    {
        var root = RepoRoot();
        var benchReadme = Path.Combine(root, "benchmarks", "Ferret.Benchmarks", "README.md");
        var rootReadme = Path.Combine(root, "README.md");

        var docPath = File.Exists(benchReadme) ? benchReadme : rootReadme;
        File.Exists(docPath).Should().BeTrue(
            "the benchmark run instructions must live in benchmarks/Ferret.Benchmarks/README.md or README.md");

        var doc = File.ReadAllText(docPath);

        doc.Should().Contain("dotnet run -c Release --project benchmarks/Ferret.Benchmarks");
        doc.Should().Contain("Docker");
        doc.Should().Contain("FERRET_POSTGRES_IMAGE");
        doc.Should().Contain("skipped");
    }
}
