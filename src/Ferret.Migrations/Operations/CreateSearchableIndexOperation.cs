using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Ferret.Migrations.Operations;

/// <summary>
/// Creates a search index on a column. <see cref="IndexSql"/> is the verbatim DDL
/// supplied by the matching <see cref="Ferret.Abstractions.Search.ISearchBackend"/> at
/// <c>OnModelCreating</c> time — already idempotent (<c>CREATE INDEX CONCURRENTLY IF NOT EXISTS</c>).
/// </summary>
public sealed class CreateSearchableIndexOperation : MigrationOperation
{
    public required string IndexName { get; init; }
    public required string TableName { get; init; }
    public required string ColumnName { get; init; }
    public required string IndexSql { get; init; }
}
