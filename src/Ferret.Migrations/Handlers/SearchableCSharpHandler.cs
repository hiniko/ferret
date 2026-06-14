using EntityFrameworkCore.ExtensibleMigrations;
using Ferret.Core.Backends.FullText;
using Ferret.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Ferret.Migrations.Handlers;

[CustomMigrationHandler(Order = 100)]
public sealed class SearchableCSharpHandler : ICSharpMigrationOperationHandler
{
    public bool CanHandle(MigrationOperation operation) =>
        operation is CreateSearchableIndexOperation
                  or DropSearchableIndexOperation
                  or EnsurePgTrgmExtensionOperation
                  or EnsureSidecarTableOperation
                  or EnsureReindexJobsTableOperation
                  or CreateFullTextGroupOperation
                  or AlterFullTextGroupOperation
                  or DropFullTextGroupOperation
                  or CreateJoinedTableTriggerOperation
                  or DropJoinedTableTriggerOperation;

    public OperationPhase Phase(MigrationOperation operation) => operation switch
    {
        EnsurePgTrgmExtensionOperation => OperationPhase.BeforeCore,
        DropSearchableIndexOperation   => OperationPhase.BeforeCore,
        EnsureSidecarTableOperation    => OperationPhase.BeforeCore,
        EnsureReindexJobsTableOperation => OperationPhase.BeforeCore,
        DropFullTextGroupOperation     => OperationPhase.BeforeCore,
        DropJoinedTableTriggerOperation => OperationPhase.BeforeCore,
        CreateSearchableIndexOperation => OperationPhase.AfterCore,
        CreateFullTextGroupOperation   => OperationPhase.AfterCore,
        AlterFullTextGroupOperation    => OperationPhase.AfterCore,
        CreateJoinedTableTriggerOperation => OperationPhase.AfterCore,
        _                              => OperationPhase.AfterCore,
    };

    public void Generate(MigrationOperation operation, IndentedStringBuilder builder)
    {
        switch (operation)
        {
            case EnsurePgTrgmExtensionOperation ext:
                EmitSql(builder,
                    $"CREATE EXTENSION IF NOT EXISTS \"{Escape(ext.ExtensionName)}\";",
                    suppressTransaction: false);
                break;
            case CreateSearchableIndexOperation create:
                EmitSql(builder, create.IndexSql, suppressTransaction: true);
                break;
            case DropSearchableIndexOperation drop:
                EmitSql(builder,
                    $"DROP INDEX CONCURRENTLY IF EXISTS \"{Escape(drop.IndexName)}\";",
                    suppressTransaction: true);
                break;
            case EnsureSidecarTableOperation sidecar:
                EmitSql(builder,
                    sidecar.KeyParts.Count > 1
                        ? FullTextDdlBuilder.CreateSidecarTable(
                            sidecar.SidecarTable, sidecar.SidecarSchema,
                            sidecar.SourceTable,  sidecar.SourceSchema,
                            sidecar.KeyParts.Select(k => (k.ColumnName, k.ColumnType)).ToList())
                        : FullTextDdlBuilder.CreateSidecarTable(
                            sidecar.SidecarTable, sidecar.SidecarSchema,
                            sidecar.SourceTable,  sidecar.SourceSchema,
                            sidecar.IdColumn, sidecar.IdColumnType),
                    suppressTransaction: false);
                break;
            case EnsureReindexJobsTableOperation:
                EmitSql(builder, FullTextDdlBuilder.EnsureReindexJobsTable(), suppressTransaction: false);
                break;
            case CreateFullTextGroupOperation createFt:
                EmitCreateGroup(builder, createFt);
                break;
            case AlterFullTextGroupOperation alterFt:
                EmitAlterGroup(builder, alterFt);
                break;
            case DropFullTextGroupOperation dropFt:
                EmitDropGroup(builder, dropFt);
                break;
            case CreateJoinedTableTriggerOperation createJoined:
                EmitCreateJoinedTableTrigger(builder, createJoined);
                break;
            case DropJoinedTableTriggerOperation dropJoined:
                EmitDropJoinedTableTrigger(builder, dropJoined);
                break;
        }
    }

    private static void EmitCreateJoinedTableTrigger(IndentedStringBuilder builder, CreateJoinedTableTriggerOperation op)
    {
        var functionName = FullTextSidecarNaming.ChangeTrackingFunctionName(op.SourceTable, op.JoinedTable, op.JoinedSchema);
        var triggerName  = FullTextSidecarNaming.ChangeTrackingTriggerName(op.SourceTable, op.JoinedTable, op.JoinedSchema);

        EmitSql(builder,
            FullTextDdlBuilder.CreateChangeTrackingFunctionAndTrigger(
                op.JoinedTable, op.JoinedSchema,
                op.SourceTable, op.SourceSchema,
                new[] { op.IdColumn },
                op.JoinPath, functionName, triggerName,
                op.Entity, op.GroupName),
            suppressTransaction: false);
    }

    private static void EmitDropJoinedTableTrigger(IndentedStringBuilder builder, DropJoinedTableTriggerOperation op)
    {
        var functionName = FullTextSidecarNaming.ChangeTrackingFunctionName(op.SourceTable, op.JoinedTable, op.JoinedSchema);
        var triggerName  = FullTextSidecarNaming.ChangeTrackingTriggerName(op.SourceTable, op.JoinedTable, op.JoinedSchema);

        EmitSql(builder,
            FullTextDdlBuilder.DropChangeTrackingFunctionAndTrigger(
                op.JoinedTable, op.JoinedSchema, functionName, triggerName),
            suppressTransaction: false);
    }

    private static void EmitCreateGroup(IndentedStringBuilder builder, CreateFullTextGroupOperation op)
    {
        var functionName = FullTextSidecarNaming.SyncFunctionName(op.SourceTable);
        var triggerName  = FullTextSidecarNaming.SyncTriggerName(op.SourceTable);
        var columnName   = op.Group.Name + op.ColumnSuffix;
        var indexName    = FullTextSidecarNaming.IndexName(op.SidecarTable, columnName);
        var keyColumns   = ResolveKeyColumns(op.KeyColumns, op.IdColumn);

        EmitSql(builder,
            FullTextDdlBuilder.AddGroupColumn(op.SidecarTable, op.SidecarSchema, columnName),
            suppressTransaction: false);

        EmitSql(builder,
            FullTextDdlBuilder.CreateGroupIndex(op.SidecarTable, op.SidecarSchema, indexName, columnName),
            suppressTransaction: false);

        EmitSql(builder,
            FullTextDdlBuilder.CreateSyncFunctionAndTrigger(
                op.SidecarTable, op.SidecarSchema,
                op.SourceTable,  op.SourceSchema,
                keyColumns, functionName, triggerName,
                op.ColumnSuffix, op.AllGroupsAfter),
            suppressTransaction: false);

        switch (op.ReindexMode)
        {
            case ReindexMode.Inline:
                EmitSql(builder,
                    FullTextDdlBuilder.Backfill(
                        op.SidecarTable, op.SidecarSchema,
                        op.SourceTable,  op.SourceSchema,
                        keyColumns, op.ColumnSuffix, op.AllGroupsAfter),
                    suppressTransaction: false);
                break;
            case ReindexMode.Concurrent:
                EmitSql(builder,
                    FullTextDdlBuilder.EnqueueReindexJob(op.Entity, op.Group.Name, op.ConcurrentBatchSize),
                    suppressTransaction: false);
                break;
            case ReindexMode.Deferred:
                break;
        }
    }

    private static void EmitAlterGroup(IndentedStringBuilder builder, AlterFullTextGroupOperation op)
    {
        var functionName = FullTextSidecarNaming.SyncFunctionName(op.SourceTable);
        var triggerName  = FullTextSidecarNaming.SyncTriggerName(op.SourceTable);
        var keyColumns   = ResolveKeyColumns(op.KeyColumns, op.IdColumn);

        EmitSql(builder,
            FullTextDdlBuilder.CreateSyncFunctionAndTrigger(
                op.SidecarTable, op.SidecarSchema,
                op.SourceTable,  op.SourceSchema,
                keyColumns, functionName, triggerName,
                op.ColumnSuffix, op.AllGroupsAfter),
            suppressTransaction: false);

        switch (op.ReindexMode)
        {
            case ReindexMode.Inline:
                EmitSql(builder,
                    FullTextDdlBuilder.Backfill(
                        op.SidecarTable, op.SidecarSchema,
                        op.SourceTable,  op.SourceSchema,
                        keyColumns, op.ColumnSuffix, op.AllGroupsAfter),
                    suppressTransaction: false);
                break;
            case ReindexMode.Concurrent:
                EmitSql(builder,
                    FullTextDdlBuilder.EnqueueReindexJob(op.Entity, op.Group.Name, op.ConcurrentBatchSize),
                    suppressTransaction: false);
                break;
            case ReindexMode.Deferred:
                break;
        }
    }

    private static void EmitDropGroup(IndentedStringBuilder builder, DropFullTextGroupOperation op)
    {
        var functionName = FullTextSidecarNaming.SyncFunctionName(op.SourceTable);
        var triggerName  = FullTextSidecarNaming.SyncTriggerName(op.SourceTable);
        var columnName   = op.GroupName + op.ColumnSuffix;
        var indexName    = FullTextSidecarNaming.IndexName(op.SidecarTable, columnName);
        var keyColumns   = ResolveKeyColumns(op.KeyColumns, op.IdColumn);

        EmitSql(builder,
            FullTextDdlBuilder.DropGroupIndex(indexName),
            suppressTransaction: false);

        EmitSql(builder,
            FullTextDdlBuilder.DropGroupColumn(op.SidecarTable, op.SidecarSchema, columnName),
            suppressTransaction: false);

        EmitSql(builder,
            FullTextDdlBuilder.CreateSyncFunctionAndTrigger(
                op.SidecarTable, op.SidecarSchema,
                op.SourceTable,  op.SourceSchema,
                keyColumns, functionName, triggerName,
                op.ColumnSuffix, op.AllGroupsAfter),
            suppressTransaction: false);
    }

    private static IReadOnlyList<string> ResolveKeyColumns(IReadOnlyList<string> keyColumns, string idColumn) =>
        keyColumns.Count > 0 ? keyColumns : new[] { idColumn };

    private static void EmitSql(IndentedStringBuilder builder, string sql, bool suppressTransaction)
    {
        builder.AppendLine();
        builder.Append("migrationBuilder.Sql(\"\"\"");
        builder.AppendLine();
        using (builder.Indent())
        {
            foreach (var line in sql.Split('\n'))
            {
                builder.AppendLine(line.TrimEnd('\r'));
            }
        }
        builder.Append("\"\"\"");
        if (suppressTransaction)
        {
            builder.Append(", suppressTransaction: true");
        }
        builder.AppendLine(");");
    }

    private static string Escape(string identifier) => identifier.Replace("\"", "\"\"");
}
