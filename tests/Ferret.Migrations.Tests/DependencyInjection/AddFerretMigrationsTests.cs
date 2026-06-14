using EntityFrameworkCore.ExtensibleMigrations;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Ferret.Migrations.Tests.DependencyInjection;

public class AddFerretMigrationsTests
{
    [Fact]
    public void Registers_three_handler_interfaces()
    {
        var services = new ServiceCollection();
        services.AddFerretMigrations();
        var sp = services.BuildServiceProvider();

        sp.GetServices<IMigrationOperationHandler>()
            .Should().ContainSingle()
            .Which.Should().BeOfType<SearchableMigrationOperationHandler>();

        sp.GetServices<ICSharpMigrationOperationHandler>()
            .Should().ContainSingle()
            .Which.Should().BeOfType<SearchableCSharpHandler>();

        sp.GetServices<IMigrationsSnapshotHandler>()
            .Should().ContainSingle()
            .Which.Should().BeOfType<SearchableSnapshotHandler>();
    }
}
