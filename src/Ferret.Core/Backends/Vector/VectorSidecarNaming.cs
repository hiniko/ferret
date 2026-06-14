namespace Ferret.Core.Backends.Vector;

public static class VectorSidecarNaming
{
    /// <summary>The single active embedding version in v1 of the foundation.</summary>
    public const int CurrentVersion = 1;

    public static string TableName(string sourceTable, VectorOptions options) => sourceTable + options.SidecarSuffix;

    public static string ColumnName(string groupName, string columnSuffix, int version) =>
        $"{groupName}{columnSuffix}_v{version}";

    public static string ColumnName(string groupName, VectorOptions options, int version) =>
        ColumnName(groupName, options.ColumnSuffix, version);

    public static string IndexName(string sidecarTable, string column) => $"ix_{sidecarTable}_{column}_hnsw";

    public static string SyncJobEntity(string sourceTable) => sourceTable;
}
