using Ferret.Abstractions.Attributes;
using Ferret.Core.DependencyInjection;
using Ferret.Core.Engine;
using Ferret.EntityFrameworkCore.DependencyInjection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ferret.EntityFrameworkCore.Tests;

public sealed class EfCompositeKeyAutoFillRuntimeTests
{
    // Composite-key entity WITHOUT an explicit KeyProperties attribute: the EF
    // primary key (TenantId, Id) must auto-fill into the runtime EntityRegistry.
    [SearchableEntity(Table = "ef_tenant_docs")]
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
        }
    }

    [Fact]
    public void Composite_ef_key_without_attribute_auto_fills_into_runtime_registry()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<TestContext>(o => o.UseNpgsql("Host=localhost;Database=ferret_test"));
        services.AddFerret(opts => opts
            .ScanAssembly(typeof(EfTenantDoc).Assembly));
        services.AddFerretEntityFrameworkCore<TestContext>();

        using var sp = services.BuildServiceProvider();
        var registry = sp.GetRequiredService<EntityRegistry>();
        var model = registry.Get<EfTenantDoc>();

        model.IsComposite.Should().BeTrue();
        model.Key.Select(k => k.PropertyName).Should().Equal("TenantId", "Id");
        model.Key.Select(k => k.ColumnName).Should().Equal("tenant_id", "id");
    }

    // The documented registration path (README:304) is AddFerretEntityFrameworkQueryService;
    // it must register the key-override source too so composite EF PKs auto-fill.
    [Fact]
    public void Composite_ef_key_auto_fills_through_documented_query_service_registration()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<TestContext>(o => o.UseNpgsql("Host=localhost;Database=ferret_test"));
        services.AddFerret(opts => opts
            .ScanAssembly(typeof(EfTenantDoc).Assembly));
        services.AddFerretEntityFrameworkQueryService<TestContext>();

        using var sp = services.BuildServiceProvider();
        var registry = sp.GetRequiredService<EntityRegistry>();
        var model = registry.Get<EfTenantDoc>();

        model.IsComposite.Should().BeTrue();
        model.Key.Select(k => k.PropertyName).Should().Equal("TenantId", "Id");
        model.Key.Select(k => k.ColumnName).Should().Equal("tenant_id", "id");
    }

    // Both registrations together must not double-register the override source.
    [Fact]
    public void Both_registrations_register_single_override_source()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<TestContext>(o => o.UseNpgsql("Host=localhost;Database=ferret_test"));
        services.AddFerret(opts => opts
            .ScanAssembly(typeof(EfTenantDoc).Assembly));
        services.AddFerretEntityFrameworkQueryService<TestContext>();
        services.AddFerretEntityFrameworkCore<TestContext>();

        using var sp = services.BuildServiceProvider();
        sp.GetServices<IEntityKeyOverrideSource>().Should().ContainSingle();
    }
}
