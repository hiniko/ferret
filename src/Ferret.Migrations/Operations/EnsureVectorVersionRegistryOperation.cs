using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Ferret.Migrations.Operations;

public sealed class EnsureVectorVersionRegistryOperation : MigrationOperation
{
    public string? Schema { get; init; }
}
