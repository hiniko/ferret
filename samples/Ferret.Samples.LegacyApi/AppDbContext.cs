using Ferret.Migrations;
using Microsoft.EntityFrameworkCore;

namespace Ferret.Example.LegacyApi;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Product>(e =>
        {
            e.ToTable("products");
            e.HasKey(p => p.Id);
            e.Property(p => p.Id).HasColumnName("id");
            e.Property(p => p.Name).HasColumnName("name");
            e.Property(p => p.Sku).HasColumnName("sku");
            e.Property(p => p.Category).HasColumnName("category");
            e.Property(p => p.Price).HasColumnName("price").HasColumnType("numeric(12,2)");
            e.Property(p => p.Stock).HasColumnName("stock");
            e.Property(p => p.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        });

        modelBuilder.UseFerretSearchableAnnotations(typeof(Product).Assembly);
    }
}
