using EntityFrameworkCore.ExtensibleMigrations;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace Ferret.Migrations.IntegrationTests;

public sealed class MigDesignTimeServices : IDesignTimeServices
{
    public void ConfigureDesignTimeServices(IServiceCollection services)
    {
        // ExtensibleMigrationsDesignTimeServices wraps IMigrationsModelDiffer and
        // auto-discovers handlers via [CustomMigrationHandler] on all loaded assemblies,
        // which includes SearchableMigrationOperationHandler from Ferret.Migrations.
        // We do NOT call AddFerretMigrations() here because that would double-register
        // the handlers (explicit + auto-discovery), producing duplicate operations.
        new ExtensibleMigrationsDesignTimeServices().ConfigureDesignTimeServices(services);
    }
}
