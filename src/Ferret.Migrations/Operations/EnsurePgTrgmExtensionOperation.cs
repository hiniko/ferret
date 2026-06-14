using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Ferret.Migrations.Operations;

/// <summary>
/// Ensures a Postgres extension is installed in the target database. v1 ships with the
/// default <c>pg_trgm</c>, but the operation is generic so future backends (full-text,
/// pgvector) can reuse it.
/// </summary>
public sealed class EnsurePgTrgmExtensionOperation : MigrationOperation
{
    public string ExtensionName { get; init; } = "pg_trgm";
}
