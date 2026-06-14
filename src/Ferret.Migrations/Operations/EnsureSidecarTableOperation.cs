using Ferret.Abstractions.Search;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Ferret.Migrations.Operations;

/// <summary>
/// Ensures the fulltext sidecar table exists for a source entity table.
/// Idempotent — emits <c>CREATE TABLE IF NOT EXISTS</c>.
/// </summary>
public sealed class EnsureSidecarTableOperation : MigrationOperation
{
    public required string SidecarTable { get; init; }
    public string? SidecarSchema { get; init; }
    public required string SourceTable { get; init; }
    public string? SourceSchema { get; init; }
    public required string IdColumn { get; init; }
    public required string IdColumnType { get; init; }
    public IReadOnlyList<KeyPart> KeyParts { get; init; } = [];

    public sealed class KeyPart
    {
        public required string ColumnName { get; init; }
        public required string ColumnType { get; init; }
    }
}
