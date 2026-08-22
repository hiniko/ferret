using Ferret.Core.DependencyInjection;
using Ferret.Core.Engine;
using Ferret.Migrations.Annotations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ferret.EntityFrameworkCore.Tests;

// Proves EntityFrameworkKeyOverrideSource's AsyncLocal re-entrancy guard (added in 72a00f9)
// does not silently drop composite-key overrides for the real app registration pattern:
// modelBuilder.UseFerretSearchableAnnotations(this, assembly) inside OnModelCreating.
// That call resolves EntityRegistry from DI (FerretMigrationsModelBuilderExtensions.cs:316),
// which re-enters EntityFrameworkKeyOverrideSource.GetKeyOverrides while the outer
// AddSingleton(BuildRegistry) factory is still running for the same singleton.
public sealed class EfCompositeKeyReentrantModelBuildingTests
{
    // Composite-key entity WITHOUT an explicit KeyProperties attribute: the EF
    // primary key (TenantId, Id) must auto-fill into the runtime EntityRegistry
    // even when OnModelCreating itself triggers the re-entrant DI resolution.
    [SearchableEntity(Table = "ef_tenant_docs_reentrant")]
    public sealed class EfTenantDoc
    {
        public Guid TenantId { get; init; }
        public long Id { get; init; }
        public string Body { get; init; } = "";
    }

    private sealed class TestContext : DbContext
    {
        public TestContext(DbContextOptions<TestContext> opts) : base(opts) { }
        public DbSet<EfTenantDoc> Docs => Set<EfTenantDoc>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<EfTenantDoc>().HasKey(e => new { e.TenantId, e.Id });

            // The real-app registration pattern (see samples/Ferret.Samples.ExistingApi/AppDbContext.cs).
            // Resolves EntityRegistry from the application service provider, re-entering
            // EntityFrameworkKeyOverrideSource<TestContext> while EntityRegistry's own
            // AddSingleton(BuildRegistry) factory is still on the stack.
            modelBuilder.UseFerretSearchableAnnotations(this, typeof(EfTenantDoc).Assembly);
        }
    }

    [Fact]
    public void Composite_ef_key_survives_reentrant_UseFerretSearchableAnnotations_registration()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFerret(opts => opts
            .ScanAssembly(typeof(EfTenantDoc).Assembly)
            .UseTrigramSearch());
        services.AddDbContext<TestContext>((sp, o) => o
            .UseNpgsql("Host=localhost;Database=ferret_test")
            .UseApplicationServiceProvider(sp));
        services.AddFerretEntityFrameworkCore<TestContext>();

        using var sp = services.BuildServiceProvider();

        // Force OnModelCreating (and therefore the re-entrant resolution) by touching the
        // context's model, the same way EF itself does on first use.
        using (var scope = sp.CreateScope())
        {
            _ = scope.ServiceProvider.GetRequiredService<TestContext>().Model;
        }

        var registry = sp.GetRequiredService<EntityRegistry>();
        var model = registry.Get<EfTenantDoc>();

        model.IsComposite.Should().BeTrue();
        model.Key.Select(k => k.PropertyName).Should().Equal("TenantId", "Id");
        model.Key.Select(k => k.ColumnName).Should().Equal("tenant_id", "id");
    }
}
