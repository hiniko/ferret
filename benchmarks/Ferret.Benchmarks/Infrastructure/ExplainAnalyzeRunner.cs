using System.Text.Json;
using Ferret.Abstractions.Search;
using Npgsql;

namespace Ferret.Benchmarks.Infrastructure;

public sealed class ExplainScanNode
{
    public required string NodeType { get; init; }
    public string? RelationName { get; init; }
    public bool IsIndexScan => NodeType is "Index Scan" or "Index Only Scan" or "Bitmap Index Scan";
    public bool IsSeqScan => NodeType == "Seq Scan";
}

public sealed class ExplainAnalyzeResult
{
    public required double? TotalCost { get; init; }
    public required double? ActualTotalTimeMs { get; init; }
    public required IReadOnlyList<ExplainScanNode> Scans { get; init; }
}

public static class ExplainAnalyzeRunner
{
    public static async Task<ExplainAnalyzeResult> RunAsync(string connectionString, SearchSqlFragment fragment)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) " + fragment.Sql;
        foreach (var p in fragment.Parameters)
            cmd.Parameters.AddWithValue(p.Key, p.Value ?? DBNull.Value);

        var json = (string)(await cmd.ExecuteScalarAsync())!;

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement[0];
        var plan = root.GetProperty("Plan");

        var totalCost = plan.TryGetProperty("Total Cost", out var tc) ? tc.GetDouble() : (double?)null;
        var actualTime = plan.TryGetProperty("Actual Total Time", out var at) ? at.GetDouble() : (double?)null;

        var scans = new List<ExplainScanNode>();
        CollectScans(plan, scans);

        return new ExplainAnalyzeResult
        {
            TotalCost = totalCost,
            ActualTotalTimeMs = actualTime,
            Scans = scans,
        };
    }

    private static void CollectScans(JsonElement node, List<ExplainScanNode> scans)
    {
        var nodeType = node.GetProperty("Node Type").GetString() ?? "";
        if (nodeType.Contains("Scan", StringComparison.Ordinal))
        {
            scans.Add(new ExplainScanNode
            {
                NodeType = nodeType,
                RelationName = node.TryGetProperty("Relation Name", out var rn) ? rn.GetString() : null,
            });
        }

        if (node.TryGetProperty("Plans", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
                CollectScans(child, scans);
        }
    }
}
