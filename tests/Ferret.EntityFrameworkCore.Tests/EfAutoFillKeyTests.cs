using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Ferret.EntityFrameworkCore.Tests;

public class EfAutoFillKeyTests
{
    public sealed class TenantDoc
    {
        public Guid TenantId { get; init; }
        public long DocId { get; init; }
        public string Body { get; init; } = "";
    }

    public sealed class Widget
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = "";
    }

    private sealed class TestContext : DbContext
    {
        public TestContext(DbContextOptions<TestContext> opts) : base(opts) { }
        public DbSet<TenantDoc> TenantDocs => Set<TenantDoc>();
        public DbSet<Widget> Widgets => Set<Widget>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<TenantDoc>().HasKey(e => new { e.TenantId, e.DocId });
            modelBuilder.Entity<Widget>().HasKey(e => e.Id);
        }
    }

    private static IModel BuildModel()
    {
        var opts = new DbContextOptionsBuilder<TestContext>()
            .UseNpgsql("Host=localhost;Database=ferret_test")
            .Options;
        using var ctx = new TestContext(opts);
        return ctx.Model;
    }

    [Fact]
    public void EfAutoFill_populates_KeyProperties_from_primary_key()
    {
        var model = BuildModel();
        var entity = model.FindEntityType(typeof(TenantDoc))!;

        var attr = new Ferret.Abstractions.Attributes.SearchableEntityAttribute();
        var keys = DependencyInjection.FerretEntityFrameworkExtensions.ResolveKeyProperties(entity, attr);

        keys.Should().Equal("TenantId", "DocId");
    }

    [Fact]
    public void EfAutoFill_respects_explicit_KeyProperties()
    {
        var model = BuildModel();
        var entity = model.FindEntityType(typeof(TenantDoc))!;

        var attr = new Ferret.Abstractions.Attributes.SearchableEntityAttribute
        {
            KeyProperties = ["DocId"],
        };
        var keys = DependencyInjection.FerretEntityFrameworkExtensions.ResolveKeyProperties(entity, attr);

        keys.Should().Equal("DocId");
    }

    [Fact]
    public void EfAutoFill_single_key_from_primary_key()
    {
        var model = BuildModel();
        var entity = model.FindEntityType(typeof(Widget))!;

        var keys = DependencyInjection.FerretEntityFrameworkExtensions.ResolveKeyProperties(entity, null);

        keys.Should().Equal("Id");
    }
}
