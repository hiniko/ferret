using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Ferret.Migrations.Operations;

/// <summary>
/// Drops a search index by name. Emits <c>DROP INDEX CONCURRENTLY IF EXISTS</c>.
/// </summary>
public sealed class DropSearchableIndexOperation : MigrationOperation
{
    public required string IndexName { get; init; }
}
