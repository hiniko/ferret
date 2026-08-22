using Ferret.Example.ExistingApi.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ferret.Example.ExistingApi;

/// <summary>
/// A pre-existing DbContext with several entities and its own conventions.
/// Exactly one line was added for Ferret; see OnModelCreating.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<Organisation> Organisations => Set<Organisation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ---- pre-existing configuration, unchanged ----
        modelBuilder.Entity<Organisation>(e =>
        {
            e.ToTable("organisations");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.IsDeleted).HasColumnName("is_deleted");
        });

        modelBuilder.Entity<Ticket>(e =>
        {
            e.ToTable("tickets");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Subject).HasColumnName("subject");
            e.Property(x => x.Body).HasColumnName("body");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.OrganisationId).HasColumnName("organisation_id");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.IsDeleted).HasColumnName("is_deleted");
            e.HasOne(x => x.Organisation).WithMany().HasForeignKey(x => x.OrganisationId);

            // Pre-existing global query filter. Ferret does not see this — which is
            // exactly why TicketRepository uses the candidate-keys pattern.
            e.HasQueryFilter(x => !x.IsDeleted);
        });

        // ---- the one line added for Ferret ----
        modelBuilder.UseFerretSearchableAnnotations(this, typeof(Ticket).Assembly);
    }
}
