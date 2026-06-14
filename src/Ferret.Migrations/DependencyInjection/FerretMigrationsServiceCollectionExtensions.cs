using EntityFrameworkCore.ExtensibleMigrations;
using Microsoft.Extensions.DependencyInjection;

namespace Ferret.Migrations.DependencyInjection;

public static class FerretMigrationsServiceCollectionExtensions
{
    /// <summary>
    /// Registers Ferret's three migration handlers with the design-time service collection.
    /// Call from your <c>IDesignTimeServices.ConfigureDesignTimeServices(IServiceCollection)</c>
    /// implementation alongside <c>services.AddExtensibleMigrationsFromAssembly(...)</c>.
    /// </summary>
    public static IServiceCollection AddFerretMigrations(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddMigrationOperationHandler<SearchableMigrationOperationHandler>();
        services.AddCSharpMigrationOperationHandler<SearchableCSharpHandler>();
        services.AddMigrationsSnapshotHandler<SearchableSnapshotHandler>();
        return services;
    }
}
