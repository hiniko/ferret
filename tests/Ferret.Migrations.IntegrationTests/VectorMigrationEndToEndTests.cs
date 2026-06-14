using Dapper;
using Ferret.Abstractions;
using Ferret.Core.Backends.FullText;
using Ferret.Core.Backends.Vector;
using Ferret.Core.IntegrationTests.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Ferret.Migrations.IntegrationTests;

[SearchableEntity(Table = "vec_products")]
public sealed class VecProduct : ISearchableEntity<Guid>
{
    public Guid Id { get; init; }

    [Searchable(Backend = SearchBackend.Vector, Group = "content", EmbeddingDimensions = 8)]
    public string Description { get; init; } = "";
}

public sealed class VectorMigDbContext : DbContext
{
    public VectorMigDbContext(DbContextOptions<VectorMigDbContext> opts) : base(opts) { }

    public DbSet<VecProduct> Products => Set<VecProduct>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<VecProduct>(e =>
        {
            e.ToTable("vec_products");
            e.HasKey(p => p.Id);
            e.Property(p => p.Description).IsRequired();
        });
        modelBuilder.UseFerretSearchableAnnotations(typeof(VecProduct).Assembly);
    }
}

[Collection("pgvector")]
public class VectorMigrationEndToEndTests
{
    private readonly PgVectorFixture _fx;

    public VectorMigrationEndToEndTests(PgVectorFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Diff_emits_vector_ddl_then_apply_creates_extension_column_and_hnsw_index()
    {
        Skip.IfNot(
            Environment.GetEnvironmentVariable("FERRET_BENCH") == "1",
            "Benchmark-infrastructure test (spins a dedicated container + seeds large datasets). Set FERRET_BENCH=1 to run.");

        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();

        await conn.ExecuteAsync(@"
            DROP TABLE IF EXISTS vec_products_vec CASCADE;
            DROP TABLE IF EXISTS vec_products CASCADE;
            CREATE TABLE vec_products (id uuid PRIMARY KEY, description text NOT NULL);");

        var dbOpts = new DbContextOptionsBuilder<VectorMigDbContext>()
            .UseNpgsql(_fx.ConnectionString)
            .EnableServiceProviderCaching(false)
            .Options;
        await using var ctx = new VectorMigDbContext(dbOpts);

        var designServices = new ServiceCollection();
        designServices.AddEntityFrameworkDesignTimeServices();
        designServices.AddDbContextDesignTimeServices(ctx);
        new MigDesignTimeServices().ConfigureDesignTimeServices(designServices);
        var designProvider = designServices.BuildServiceProvider();

        var differ = designProvider.GetRequiredService<IMigrationsModelDiffer>();
        var ops = differ.GetDifferences(
            source: null,
            target: ctx.GetService<IDesignTimeModel>().Model.GetRelationalModel()).ToList();

        ops.OfType<EnsurePgvectorExtensionOperation>().Should().ContainSingle();
        ops.OfType<EnsureVectorSidecarTableOperation>().Should().ContainSingle();
        ops.OfType<CreateVectorIndexOperation>().Should().ContainSingle()
            .Which.Group.Name.Should().Be("content");
        ops.OfType<EnsureVectorVersionRegistryOperation>().Should().ContainSingle();

        foreach (var op in ops)
        {
            string? sql = op switch
            {
                EnsurePgvectorExtensionOperation => VectorDdlBuilder.EnsureExtension(),
                EnsureReindexJobsTableOperation => FullTextDdlBuilder.EnsureReindexJobsTable(),
                EnsureVectorVersionRegistryOperation r => VectorDdlBuilder.CreateVersionRegistry(r.Schema),
                EnsureVectorSidecarTableOperation s => VectorDdlBuilder.CreateSidecarTable(
                    s.SidecarTable, s.SidecarSchema, s.SourceTable, s.SourceSchema, s.IdColumn, s.IdColumnType),
                CreateVectorIndexOperation c => BuildCreateIndexSql(c),
                _ => null,
            };
            if (sql is null) continue;
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync();
        }

        (await conn.QuerySingleAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM pg_extension WHERE extname='vector')"))
            .Should().BeTrue();
        (await conn.QuerySingleAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='vec_products_vec' AND column_name='content_embedding_v1' AND udt_name='vector')"))
            .Should().BeTrue();
        (await conn.QuerySingleAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM pg_indexes WHERE indexname='ix_vec_products_vec_content_embedding_v1_hnsw')"))
            .Should().BeTrue();
        (await conn.QuerySingleAsync<bool>(
            "SELECT to_regclass('public.ferret_vector_versions') IS NOT NULL"))
            .Should().BeTrue();
    }

    private static string BuildCreateIndexSql(CreateVectorIndexOperation c)
    {
        var column = $"{c.Group.Name}{c.ColumnSuffix}_v{VectorSidecarNaming.CurrentVersion}";
        return VectorDdlBuilder.AddGroupColumn(c.SidecarTable, c.SidecarSchema, column, c.Group.Dimensions)
             + VectorDdlBuilder.CreateGroupIndex(c.SidecarTable, c.SidecarSchema,
                 VectorSidecarNaming.IndexName(c.SidecarTable, column), column, c.HnswM, c.HnswEfConstruction);
    }
}
