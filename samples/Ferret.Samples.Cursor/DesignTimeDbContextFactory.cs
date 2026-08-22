using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace Ferret.Example.Cursor;

/// <summary>
/// Used by `dotnet ef`. EF calls this instead of booting Program.cs, so migration commands
/// do not start the web host or hosted services — and the provider built here gives Ferret
/// the same options the app configures at runtime, so generated DDL matches the queries.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    // Design-time tools do not read appsettings; honour the same env var the app uses so
    // `dotnet ef` can target a non-default database.
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
        ?? "Host=localhost;Port=5433;Database=ferret_samples;Username=postgres;Password=ferret";

    public AppDbContext CreateDbContext(string[] args)
    {
        var services = new ServiceCollection();
        services.AddFerret(o => o
            .ScanAssembly(typeof(Product).Assembly)
            .UseTrigramSearch()
            .WithPaginationDefaults(defaultLimit: 25, maxLimit: 100));

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .UseApplicationServiceProvider(services.BuildServiceProvider())
            .Options;

        return new AppDbContext(options);
    }
}
