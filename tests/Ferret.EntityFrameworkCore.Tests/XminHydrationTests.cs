using Ferret.Core.DependencyInjection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Ferret.EntityFrameworkCore.Tests;

/// <summary>
/// Bug 1 regression: EF hydrator emitted SELECT * which excluded the xmin system column,
/// breaking entities that use .IsRowVersion() (the Npgsql xmin concurrency token).
/// </summary>
[Collection("postgres")]
public sealed class XminHydrationTests
{
    private readonly PostgresFixture _fixture;

    public XminHydrationTests(PostgresFixture fixture) => _fixture = fixture;

    [SearchableEntity(Table = "xmin_docs")]
    public sealed class XminDoc
    {
        public Guid Id { get; init; }
        [Searchable] public string Body { get; init; } = "";
        public uint Version { get; init; }   // mapped via .IsRowVersion() → xmin
    }

    private sealed class XminContext : DbContext
    {
        public XminContext(DbContextOptions<XminContext> opts) : base(opts) { }
        public DbSet<XminDoc> Docs => Set<XminDoc>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<XminDoc>(b =>
            {
                b.ToTable("xmin_docs");
                b.HasKey(e => e.Id);
                b.Property(e => e.Id).HasColumnName("id");
                b.Property(e => e.Body).HasColumnName("body");
                // IsRowVersion() maps Version → xmin system column (not a real table column).
                b.Property(e => e.Version).IsRowVersion();
            });
        }
    }

    [Fact]
    public async Task Hydrate_xmin_entity_returns_rows_without_throwing()
    {
        var connStr = _fixture.UniqueConnectionString();

        // ---- create fresh DB + schema + seed manually ----------------------
        // 1. Create the database (Npgsql EF EnsureCreatedAsync handles this).
        // 2. Install pg_trgm extension and create trigram index for Ferret search.
        var opts = new DbContextOptionsBuilder<XminContext>()
            .UseNpgsql(connStr)
            .Options;

        await using (var ctx = new XminContext(opts))
        {
            await ctx.Database.EnsureCreatedAsync();
        }

        // Install pg_trgm and create trigram GiST index (required by UseTrigramSearch).
        await using (var conn = new NpgsqlConnection(connStr))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE EXTENSION IF NOT EXISTS pg_trgm;
                CREATE INDEX IF NOT EXISTS ix_xmin_docs_body_gist_trgm
                    ON xmin_docs USING gist ((body::text) gist_trgm_ops);
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        // Seed rows.
        await using (var ctx = new XminContext(opts))
        {
            ctx.Docs.AddRange(
                new XminDoc { Id = Guid.NewGuid(), Body = "ferret search test alpha" },
                new XminDoc { Id = Guid.NewGuid(), Body = "ferret search test beta" }
            );
            await ctx.SaveChangesAsync();
        }

        // ---- run Ferret search via EF session ---------------------------------
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<XminContext>(o => o.UseNpgsql(connStr));
        services.AddFerret(opts2 => opts2
            .ScanAssembly(typeof(XminDoc).Assembly)
            .UseTrigramSearch());
        services.AddFerretEntityFrameworkCore<XminContext>();

        await using var sp = services.BuildServiceProvider();

        using var scope = sp.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<XminContext>();
        var engineSp = scope.ServiceProvider;

        var query = new PagedQuery<XminDoc, Guid>
        {
            Mode = PaginationMode.Offset,
            Search = "ferret",
            Limit = 25,
        };

        // This used to throw: column s.xmin does not exist
        var result = await context.SearchOffsetAsync<XminDoc, Guid>(engineSp, query);

        result.Items.Should().NotBeEmpty("trigram search on 'ferret' should match both seeded rows");
        result.Items.Should().AllSatisfy(doc => doc.Body.Should().Contain("ferret"));
    }
}
