using System.Diagnostics;
using System.Reflection;

namespace Ferret.Core.Diagnostics;

/// <summary>
/// Public diagnostics surface for Ferret. Subscribe with OpenTelemetry by adding
/// <see cref="ActivitySourceName"/> to your tracer builder, or attach an
/// <see cref="ActivityListener"/> directly.
/// </summary>
public static class FerretDiagnostics
{
    /// <summary>Name of the <see cref="System.Diagnostics.ActivitySource"/> used by Ferret.</summary>
    public const string ActivitySourceName = "Ferret.Core";

    /// <summary><see cref="System.Diagnostics.ActivitySource"/> emitted by the engine. Stable name; safe to reference from consumer code.</summary>
    public static readonly ActivitySource ActivitySource = new(
        ActivitySourceName,
        typeof(FerretDiagnostics).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0");

    internal static class Tags
    {
        public const string Entity      = "ferret.entity";
        public const string Mode        = "ferret.mode";
        public const string Limit       = "ferret.limit";
        public const string Page        = "ferret.page";
        public const string HasSearch   = "ferret.search";
        public const string FilterCount = "ferret.filter.count";
        public const string SortCount   = "ferret.sort.count";
        public const string RowCount    = "ferret.row.count";
        public const string TotalCount  = "ferret.total_count";
        public const string Backend     = "ferret.backend";
        public const string CursorDir   = "ferret.cursor.direction";
        public const string DbSystem    = "db.system";
        public const string DbStatement = "db.statement";
    }
}
