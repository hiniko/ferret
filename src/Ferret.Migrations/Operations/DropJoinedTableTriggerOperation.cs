using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Ferret.Migrations.Operations;

public sealed class DropJoinedTableTriggerOperation : MigrationOperation
{
    public required string Entity { get; init; }
    public required string SidecarTable { get; init; }
    public string? SidecarSchema { get; init; }
    public required string SourceTable { get; init; }
    public string? SourceSchema { get; init; }
    public required string IdColumn { get; init; }
    public required string JoinedTable { get; init; }
    public string? JoinedSchema { get; init; }
}
