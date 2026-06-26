using Ferret.Abstractions.Attributes;
using Ferret.Abstractions.Querying;
using Ferret.Core.DependencyInjection;
using Ferret.EntityFrameworkCore.DependencyInjection;
using Ferret.EntityFrameworkCore.Querying;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ferret.EntityFrameworkCore.Tests;

/// <summary>
/// Bug 2 regression: AddFerretEntityFrameworkCore used services.AddScoped (not Replace),
/// leaving the core FerretCoreQueryService registration alive. DI ValidateOnBuild would
/// fail at startup because FerretCoreQueryService can't resolve IFerretSession (not in DI).
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
            o.UseNpgsql("Host=localhost;Database=ferret_di_test"));
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

        act.Should().NotThrow("EF adapter must replace the core IFerretQueryService so DI validation passes");
    }

    [Fact]
    public void IFerretQueryService_resolves_to_EF_implementation()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<DiTestContext>(o =>
            o.UseNpgsql("Host=localhost;Database=ferret_di_test"));
        services.AddFerret(opts => opts
            .ScanAssembly(typeof(DiTestDoc).Assembly)
            .UseTrigramSearch());
        services.AddFerretEntityFrameworkCore<DiTestContext>();

        using var sp = services.BuildServiceProvider();

        // Only ONE registration must remain after Replace.
        var descriptors = services.Where(d => d.ServiceType == typeof(IFerretQueryService)).ToList();
        descriptors.Should().ContainSingle("Replace should leave exactly one IFerretQueryService registration");
        descriptors[0].ImplementationType.Should().Be<EntityFrameworkQueryService<DiTestContext>>();

        // And it must resolve in a scope.
        using var scope = sp.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IFerretQueryService>();
        svc.Should().BeOfType<EntityFrameworkQueryService<DiTestContext>>();
    }
}
