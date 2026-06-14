using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Ferret.Migrations.Operations;

public sealed class EnsurePgvectorExtensionOperation : MigrationOperation
{
    public string ExtensionName { get; init; } = "vector";
}
