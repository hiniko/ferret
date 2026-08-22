using System.Data.Common;
using Ferret.Abstractions;
using Ferret.Abstractions.Attributes;
using Ferret.Abstractions.Querying;
using Ferret.Core.DependencyInjection;
using Ferret.Hydration.Dapper.DependencyInjection;
using Ferret.Hydration.Dapper.Querying;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ferret.Hydration.Dapper.Tests;

/// <summary>
/// Dapper users get the same injected <see cref="IFerretQueryService"/> front door as EF Core
/// users. Before AddFerretDapper existed they had to implement the interface themselves.
/// </summary>
public class RegistrationTests
{
    [SearchableEntity(Table = "widgets")]
    public sealed class Widget : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }
        [Searchable] public string Name { get; init; } = "";
    }

    private static ServiceCollection BaseServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFerret(o => o
            .ScanAssembly(typeof(Widget).Assembly)
            .UseTrigramSearch());
        return services;
    }

    [Fact]
    public void AddFerretDapper_registers_the_query_service()
    {
        var services = BaseServices();
        services.AddFerretDapper((_, _) => Unused());

        using var sp = services.BuildServiceProvider(validateScopes: true);
        using var scope = sp.CreateScope();

        scope.ServiceProvider.GetRequiredService<IFerretQueryService>()
            .Should().BeOfType<DapperQueryService>();
    }

    [Fact]
    public void Container_validates_so_missing_dependencies_fail_at_startup()
    {
        var services = BaseServices();
        services.AddFerretDapper((_, _) => Unused());

        // ValidateOnBuild is what catches an adapter that registers a service whose
        // dependencies are not themselves registered.
        var act = () => services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        act.Should().NotThrow();
    }

    [Fact]
    public void Calling_twice_leaves_one_registration()
    {
        var services = BaseServices();
        services.AddFerretDapper((_, _) => Unused());
        services.AddFerretDapper((_, _) => Unused());

        using var sp = services.BuildServiceProvider(validateScopes: true);
        using var scope = sp.CreateScope();

        scope.ServiceProvider.GetServices<IFerretQueryService>()
            .Should().ContainSingle("AddFerretDapper replaces rather than stacks");
    }

    [Fact]
    public void Connection_is_not_opened_until_a_query_runs()
    {
        var opened = false;
        var services = BaseServices();
        services.AddFerretDapper((_, _) =>
        {
            opened = true;
            return Unused();
        });

        using var sp = services.BuildServiceProvider(validateScopes: true);
        using var scope = sp.CreateScope();
        scope.ServiceProvider.GetRequiredService<IFerretQueryService>();

        opened.Should().BeFalse("resolving the service must not open a connection");
    }

    private static Task<DbConnection> Unused() =>
        throw new InvalidOperationException("connection factory should not run in these tests");
}
