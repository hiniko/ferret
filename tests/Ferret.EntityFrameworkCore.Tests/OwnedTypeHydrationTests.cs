using Ferret.Core.DependencyInjection;
using Ferret.EntityFrameworkCore;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Ferret.EntityFrameworkCore.Tests;

/// <summary>
/// Regression tests for Finding 1: OwnsOne columns on the same table were omitted from the
/// explicit projection built by EntityFrameworkHydrator.RewriteProjection. EF 10 compensates
/// at runtime by LEFT JOINing back to the source table for owned-type columns missing from the
/// subquery — so end-to-end behaviour is correct either way — but the explicit projection should
/// include all same-table owned columns to avoid the unnecessary join and to be semantically
/// correct. The unit test (Projection_includes_owned_columns) is the canonical fail/pass gate
/// for the fix; the integration test verifies the whole pipeline.
/// </summary>
[Collection("postgres")]
public sealed class OwnedTypeHydrationTests
{
    private readonly PostgresFixture _fixture;

    public OwnedTypeHydrationTests(PostgresFixture fixture) => _fixture = fixture;

    [SearchableEntity(Table = "owned_docs")]
    public sealed class OwnedDoc
    {
        public Guid Id { get; init; }
        [Searchable] public string Title { get; init; } = "";
        public DocMeta Meta { get; init; } = new();   // OwnsOne, columns on owned_docs table
    }

    public sealed class DocMeta
    {
        public string? Note { get; init; }
        public int Revision { get; init; }
    }

    private sealed class OwnedDocContext : DbContext
    {
        public OwnedDocContext(DbContextOptions<OwnedDocContext> opts) : base(opts) { }
        public DbSet<OwnedDoc> Docs => Set<OwnedDoc>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<OwnedDoc>(b =>
            {
                b.ToTable("owned_docs");
                b.HasKey(e => e.Id);
                b.Property(e => e.Id).HasColumnName("id");
                b.Property(e => e.Title).HasColumnName("title");
                b.OwnsOne(e => e.Meta, m =>
                {
                    m.Property(x => x.Note).HasColumnName("meta_note");
                    m.Property(x => x.Revision).HasColumnName("meta_revision");
                });
            });
        }
    }

    /// <summary>
    /// Unit test: confirms the explicit column projection includes owned-type columns.
    /// This test FAILS before the MappedColumns recursive fix and PASSES after it.
    /// </summary>
    [Fact]
    public void Projection_includes_owned_columns()
    {
        var opts = new DbContextOptionsBuilder<OwnedDocContext>()
            .UseInMemoryDatabase("owned_proj_test_" + Guid.NewGuid())
            .Options;

        using var ctx = new OwnedDocContext(opts);
        var hydrator = new EntityFrameworkHydrator(ctx);

        // Invoke the private RewriteProjection via reflection.
        var method = typeof(EntityFrameworkHydrator)
            .GetMethod("RewriteProjection", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .MakeGenericMethod(typeof(OwnedDoc));

        var engineSql = "SELECT * FROM owned_docs WHERE \"id\" = ANY({0})";
        var rewritten = (string)method.Invoke(hydrator, [engineSql])!;

        rewritten.Should().Contain("\"meta_note\"",
            because: "owned-type columns on the same table must appear in the explicit projection");
        rewritten.Should().Contain("\"meta_revision\"",
            because: "owned-type columns on the same table must appear in the explicit projection");
        rewritten.Should().Contain("\"id\"", because: "root entity columns must still be included");
        rewritten.Should().Contain("\"title\"", because: "root entity columns must still be included");
        rewritten.Should().NotStartWith("SELECT *",
            because: "leading SELECT * should have been replaced with explicit columns");
    }

    /// <summary>
    /// Integration test: searches an entity with an inline OwnsOne and confirms that both
    /// the root and owned-type fields are populated correctly end-to-end.
    /// </summary>
    [Fact]
    public async Task Hydrate_owned_type_entity_returns_rows_with_meta_populated()
    {
        var connStr = _fixture.UniqueConnectionString();

        var opts = new DbContextOptionsBuilder<OwnedDocContext>()
            .UseNpgsql(connStr)
            .Options;

        await using (var ctx = new OwnedDocContext(opts))
        {
            await ctx.Database.EnsureCreatedAsync();
        }

        // Install pg_trgm and create trigram index required by UseTrigramSearch.
        await using (var conn = new NpgsqlConnection(connStr))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE EXTENSION IF NOT EXISTS pg_trgm;
                CREATE INDEX IF NOT EXISTS ix_owned_docs_title_gist_trgm
                    ON owned_docs USING gist ((title::text) gist_trgm_ops);
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        // Seed rows with owned-type data populated.
        await using (var ctx = new OwnedDocContext(opts))
        {
            ctx.Docs.AddRange(
                new OwnedDoc { Id = Guid.NewGuid(), Title = "ferret owned alpha", Meta = new DocMeta { Note = "first", Revision = 1 } },
                new OwnedDoc { Id = Guid.NewGuid(), Title = "ferret owned beta",  Meta = new DocMeta { Note = "second", Revision = 2 } }
            );
            await ctx.SaveChangesAsync();
        }

        // Wire up Ferret with the EF adapter.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<OwnedDocContext>(o => o.UseNpgsql(connStr));
        services.AddFerret(opts2 => opts2
            .ScanAssembly(typeof(OwnedDoc).Assembly)
            .UseTrigramSearch());
        services.AddFerretEntityFrameworkCore<OwnedDocContext>();

        await using var sp = services.BuildServiceProvider();

        using var scope = sp.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OwnedDocContext>();
        var engineSp = scope.ServiceProvider;

        var query = new PagedQuery<OwnedDoc, Guid>
        {
            Mode = PaginationMode.Offset,
            Search = "ferret",
            Limit = 25,
        };

        var result = await context.SearchOffsetAsync<OwnedDoc, Guid>(engineSp, query);

        result.Items.Should().NotBeEmpty("trigram search on 'ferret' should match both seeded rows");
        result.Items.Should().AllSatisfy(doc =>
        {
            doc.Title.Should().Contain("ferret");
            doc.Meta.Should().NotBeNull();
            doc.Meta.Revision.Should().BeGreaterThan(0, "owned-type property must be hydrated from DB");
        });
    }
}
