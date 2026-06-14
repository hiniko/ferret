using System.Globalization;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using Ferret.Benchmarks.Infrastructure;

namespace Ferret.Benchmarks;

public sealed class BenchmarkConfig : ManualConfig
{
    public IColumn ExplainTotalCostColumn { get; } = new ExplainTotalCostColumn();

    public BenchmarkConfig()
    {
        AddExporter(MarkdownExporter.GitHub);
        AddExporter(JsonExporter.Full);
        AddColumn(ExplainTotalCostColumn);
        WithArtifactsPath("BenchmarkArtifacts");
    }
}

public sealed class ExplainTotalCostColumn : IColumn
{
    public string Id => nameof(ExplainTotalCostColumn);
    public string ColumnName => "ExplainTotalCost";
    public bool AlwaysShow => true;
    public ColumnCategory Category => ColumnCategory.Custom;
    public int PriorityInCategory => 0;
    public bool IsNumeric => true;
    public UnitType UnitType => UnitType.Dimensionless;
    public string Legend => "EXPLAIN estimated total cost reported by the PostgreSQL planner";
    public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;
    public bool IsAvailable(Summary summary) => true;

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase) =>
        GetValue(summary, benchmarkCase, summary.Style);

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style)
    {
        var depthParam = benchmarkCase.Parameters.Items
            .FirstOrDefault(p => p.Name == "Depth");
        if (depthParam?.Value is not int depth)
            return "?";

        var connectionString = SearchJoinDepthBenchmark.CurrentConnectionString;
        if (string.IsNullOrEmpty(connectionString))
            return "?";

        var fragment = GeneratedSqlCapture.Capture(depth, SearchJoinDepthBenchmark.CurrentMatchToken);
        var result = ExplainAnalyzeRunner.RunAsync(connectionString, fragment).GetAwaiter().GetResult();

        return result.TotalCost?.ToString("F2", CultureInfo.InvariantCulture) ?? "?";
    }
}
