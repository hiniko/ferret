using Ferret.Abstractions.Querying;
using Ferret.Core.DependencyInjection;
using Ferret.EntityFrameworkCore.Querying;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ferret.EntityFrameworkCore.Tests;

/// <summary>
/// AddFerret must not register any IFerretQueryService of its own — an IFerretSession is
/// per-call and never in DI, so only an adapter can supply one. The EF adapter is what
/// makes IFerretQueryService resolvable, and a dev host's ValidateOnBuild must pass.
/// </summary>
public sealed class DiValidationTests
{
    [SearchableEntity(Table = "di_test_docs")]
    public sealed class DiTestDoc
    {
        public Guid Id { get; init; }
        [Searchable] public string Body { get; init; } = "";
    }

    private sealed class DiTestContext : DbContext
    {
        public DiTestContext(DbContextOptions<DiTestContext> opts) : base(opts) { }
        public DbSet<DiTestDoc> Docs => Set<DiTestDoc>();
    }

    [Fact]
    public void BuildServiceProvider_with_ValidateOnBuild_does_not_throw()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<DiTestContext>(o =>
            o.UseInMemoryDatabase("ferret_di_test"));
        services.AddFerret(opts => opts
            .ScanAssembly(typeof(DiTestDoc).Assembly)
            .UseTrigramSearch());
        services.AddFerretEntityFrameworkCore<DiTestContext>();

        // This is what ASP.NET Core dev hosts do by default.
        var act = () => services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        act.Should().NotThrow("the EF adapter supplies a fully DI-constructable IFerretQueryService");
    }

    [Fact]
    public void AddFerret_alone_registers_no_query_service()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFerret(opts => opts
            .ScanAssembly(typeof(DiTestDoc).Assembly)
            .UseTrigramSearch());

        services.Where(d => d.ServiceType == typeof(IFerretQueryService))
            .Should().BeEmpty("AddFerret cannot supply a query service — IFerretSession is per-call, never in DI");

        // And the container must still validate: no unconstructable descriptor left behind.
        var act = () => services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
        act.Should().NotThrow();
    }

    [Fact]
    public void IFerretQueryService_resolves_to_EF_implementation()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<DiTestContext>(o =>
            o.UseInMemoryDatabase("ferret_di_test"));
        services.AddFerret(opts => opts
            .ScanAssembly(typeof(DiTestDoc).Assembly)
            .UseTrigramSearch());
        services.AddFerretEntityFrameworkCore<DiTestContext>();

        using var sp = services.BuildServiceProvider();

        // Exactly one registration — the adapter's, not a stack of them.
        var descriptors = services.Where(d => d.ServiceType == typeof(IFerretQueryService)).ToList();
        descriptors.Should().ContainSingle("the EF adapter should leave exactly one IFerretQueryService registration");
        descriptors[0].ImplementationType.Should().Be<EntityFrameworkQueryService<DiTestContext>>();

        // And it must resolve in a scope.
        using var scope = sp.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IFerretQueryService>();
        svc.Should().BeOfType<EntityFrameworkQueryService<DiTestContext>>();
    }
}
