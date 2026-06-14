using Ferret.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Ferret.Migrations.IntegrationTests;

[SearchableEntity(Table = "widgets_mig")]
public sealed class MigWidget : ISearchableEntity<Guid>
{
    public Guid Id { get; init; }
    [Searchable] public string Name { get; init; } = "";
    [Searchable] public string Sku { get; init; } = "";
}

public sealed class MigDbContext : DbContext
{
    public MigDbContext(DbContextOptions<MigDbContext> opts) : base(opts) { }
    public DbSet<MigWidget> Widgets => Set<MigWidget>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<MigWidget>(e =>
        {
            e.ToTable("widgets_mig");
            e.HasKey(w => w.Id);
            e.Property(w => w.Name).IsRequired();
            e.Property(w => w.Sku).IsRequired();
        });
        modelBuilder.UseFerretSearchableAnnotations(typeof(MigWidget).Assembly);
    }
}
