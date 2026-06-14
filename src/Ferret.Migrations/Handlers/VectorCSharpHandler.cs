using EntityFrameworkCore.ExtensibleMigrations;
using Ferret.Core.Backends.Vector;
using Ferret.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Ferret.Migrations.Handlers;

[CustomMigrationHandler(Order = 100)]
public sealed class VectorCSharpHandler : ICSharpMigrationOperationHandler
{
    public bool CanHandle(MigrationOperation operation) => operation is
        EnsurePgvectorExtensionOperation or
        EnsureVectorVersionRegistryOperation or
        EnsureVectorSidecarTableOperation or
        CreateVectorIndexOperation or
        DropVectorIndexOperation;

    public OperationPhase Phase(MigrationOperation operation) => operation switch
    {
        EnsurePgvectorExtensionOperation => OperationPhase.BeforeCore,
        EnsureVectorVersionRegistryOperation => OperationPhase.BeforeCore,
        EnsureVectorSidecarTableOperation => OperationPhase.BeforeCore,
        DropVectorIndexOperation => OperationPhase.BeforeCore,
        CreateVectorIndexOperation => OperationPhase.AfterCore,
        _ => OperationPhase.AfterCore,
    };

    public void Generate(MigrationOperation operation, IndentedStringBuilder builder)
    {
        switch (operation)
        {
            case EnsurePgvectorExtensionOperation:
                EmitSql(builder, VectorDdlBuilder.EnsureExtension(), suppressTransaction: false);
                break;
            case EnsureVectorVersionRegistryOperation regOp:
                EmitSql(builder, VectorDdlBuilder.CreateVersionRegistry(regOp.Schema), suppressTransaction: false);
                break;
            case EnsureVectorSidecarTableOperation op:
                EmitSql(builder, VectorDdlBuilder.CreateSidecarTable(
                    op.SidecarTable, op.SidecarSchema, op.SourceTable, op.SourceSchema, op.IdColumn, op.IdColumnType),
                    suppressTransaction: false);
                break;
            case CreateVectorIndexOperation op:
            {
                var column = VectorSidecarNaming.ColumnName(op.Group.Name, op.ColumnSuffix, VectorSidecarNaming.CurrentVersion);
                EmitSql(builder, VectorDdlBuilder.AddGroupColumn(op.SidecarTable, op.SidecarSchema, column, op.Group.Dimensions), suppressTransaction: false);
                EmitSql(builder, VectorDdlBuilder.CreateGroupIndex(
                    op.SidecarTable, op.SidecarSchema, VectorSidecarNaming.IndexName(op.SidecarTable, column), column, op.HnswM, op.HnswEfConstruction),
                    suppressTransaction: false);
                // Vector backfill is explicit-only in v1 (call engine.ReindexAsync<T>); embeddings
                // require an out-of-process provider the queued worker does not carry, so no enqueue here.
                break;
            }
            case DropVectorIndexOperation op:
            {
                var column = VectorSidecarNaming.ColumnName(op.Group.Name, op.ColumnSuffix, VectorSidecarNaming.CurrentVersion);
                EmitSql(builder, VectorDdlBuilder.DropGroupIndex(op.SidecarSchema, VectorSidecarNaming.IndexName(op.SidecarTable, column)), suppressTransaction: false);
                EmitSql(builder, VectorDdlBuilder.DropGroupColumn(op.SidecarTable, op.SidecarSchema, column), suppressTransaction: false);
                break;
            }
        }
    }

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
}
