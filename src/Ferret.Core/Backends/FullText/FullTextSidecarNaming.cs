namespace Ferret.Core.Backends.FullText;

public static class FullTextSidecarNaming
{
    public static string TableName(string sourceTable, FullTextOptions opts) =>
        sourceTable + opts.SidecarSuffix;

    public static string ColumnName(string group, FullTextOptions opts) =>
        group + opts.ColumnSuffix;

    public static string IndexName(string sidecarTable, string column) =>
        $"ix_{sidecarTable}_{column}_gin";

    public static string SyncFunctionName(string sourceTable) =>
        sourceTable + "_search_sync";

    public static string SyncTriggerName(string sourceTable) =>
        sourceTable + "_search_sync_t";

    public static string ChangeTrackingFunctionName(
        string ownerTable, string joinedTable, string? joinedSchema = null) =>
        joinedSchema is null
            ? ownerTable + "__" + joinedTable + "_ct"
            : ownerTable + "__" + joinedSchema + "_" + joinedTable + "_ct";

    public static string ChangeTrackingTriggerName(
        string ownerTable, string joinedTable, string? joinedSchema = null) =>
        ChangeTrackingFunctionName(ownerTable, joinedTable, joinedSchema) + "_t";
}
