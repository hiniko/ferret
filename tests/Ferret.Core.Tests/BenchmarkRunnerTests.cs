using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Json;
using Ferret.Benchmarks;
using FluentAssertions;
using Xunit;

namespace Ferret.Core.Tests;

public class BenchmarkRunnerTests
{
    [Fact]
    public void Switcher_lists_searchjoin_depth_benchmark()
    {
        var types = BenchmarkProgram.GetBenchmarkTypes();

        types.Should().Contain(typeof(SearchJoinDepthBenchmark),
            "the benchmark switcher must expose the depth-scaling benchmark");

        var config = new BenchmarkConfig();
        var exporters = config.GetExporters().ToList();

        exporters.Should().Contain(e => e is MarkdownExporter,
            "the markdown exporter must be configured so wall-clock results land in the artifact directory");
        exporters.Should().Contain(e => e is JsonExporter,
            "the json exporter must be configured so results are machine-readable in the artifact directory");

        config.ExplainTotalCostColumn.Should().NotBeNull(
            "a column for the captured EXPLAIN total cost must land alongside wall-clock results");
        config.ExplainTotalCostColumn.Id.Should().Contain("ExplainTotalCost");
    }
}
