using Ferret.Example.ExistingApi.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace Ferret.Example.ExistingApi;

/// <summary>
/// Used by `dotnet ef`. EF calls this instead of booting Program.cs, so migration commands
/// do not start the seeder or web host — and the provider built here mirrors the app's
/// AddFerret(...) call, so generated DDL matches what queries expect.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    // Design-time tools do not read appsettings; honour the same env var the app uses so
    // `dotnet ef` can target a non-default database.
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
        ?? "Host=localhost;Port=5433;Database=ferret_existing;Username=postgres;Password=ferret";

    public AppDbContext CreateDbContext(string[] args)
    {
        var services = new ServiceCollection();
        services.AddFerret(o => o
            .ScanAssembly(typeof(Ticket).Assembly)
            .UseTrigramSearch(t => t.MinimumSimilarity = 0.3)
            .WithPaginationDefaults(defaultLimit: 25, maxLimit: 100));

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .UseApplicationServiceProvider(services.BuildServiceProvider())
            .Options;

        return new AppDbContext(options);
    }
}
