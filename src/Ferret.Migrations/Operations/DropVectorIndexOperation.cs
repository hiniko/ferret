using Ferret.Abstractions.Search;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Ferret.Migrations.Operations;

public sealed class DropVectorIndexOperation : MigrationOperation
{
    public required string Entity { get; init; }
    public required string SidecarTable { get; init; }
    public string? SidecarSchema { get; init; }
    public required string ColumnSuffix { get; init; }
    public required VectorGroup Group { get; init; }
}
