using Ferret.Abstractions.Search;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Ferret.Migrations.Operations;

public sealed class DropFullTextGroupOperation : MigrationOperation
{
    public required string SidecarTable { get; init; }
    public string? SidecarSchema { get; init; }
    public required string SourceTable { get; init; }
    public string? SourceSchema { get; init; }
    public required string IdColumn { get; init; }
    public IReadOnlyList<string> KeyColumns { get; init; } = [];
    public required string ColumnSuffix { get; init; }
    public required string GroupName { get; init; }
    public required IReadOnlyList<FullTextGroup> AllGroupsAfter { get; init; }
}
