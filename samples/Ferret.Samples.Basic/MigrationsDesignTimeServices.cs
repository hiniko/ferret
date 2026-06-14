using EntityFrameworkCore.ExtensibleMigrations;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace Ferret.Example.Basic;

public sealed class MigrationsDesignTimeServices : IDesignTimeServices
{
    public void ConfigureDesignTimeServices(IServiceCollection services)
    {
        new ExtensibleMigrationsDesignTimeServices().ConfigureDesignTimeServices(services);
    }
}
