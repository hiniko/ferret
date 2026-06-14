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
    public void Bench_findings_doc_has_required_sections()
    {
        var root = RepoRoot();
        var docPath = Path.Combine(
            root, "docs", "superpowers", "specs", "2026-05-31-searchjoin-bench-findings.md");

        File.Exists(docPath).Should().BeTrue("the SearchJoin benchmark findings doc must exist");

        var doc = File.ReadAllText(docPath);

        doc.Should().Contain("## Results");
        doc.Should().Contain("## EXPLAIN ANALYZE");
        doc.Should().Contain("## Cold vs Warm");
        doc.Should().Contain("## HopBudget Recommendation");

        doc.Should().Contain("benchmarks/Ferret.Benchmarks");
        doc.Should().Contain("SearchJoinDepthBenchmark");
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
