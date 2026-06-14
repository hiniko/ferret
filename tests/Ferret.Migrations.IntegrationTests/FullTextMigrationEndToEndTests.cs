using Dapper;
using Ferret.Abstractions;
using Ferret.Core.Backends.FullText;
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

[SearchableEntity(Table = "ft_products")]
[SearchGroup("content", FullTextConfig = "english")]
public sealed class FullTextProduct : ISearchableEntity<Guid>
{
    public Guid Id { get; init; }

    [Searchable(Backend = SearchBackend.FullText, Group = "content", Weight = 2.0f)]
    public string Name { get; init; } = "";

    [Searchable(Backend = SearchBackend.FullText, Group = "content", Weight = 1.0f)]
    public string Description { get; init; } = "";
}

public sealed class FtReview
{
    public Guid Id { get; init; }
    public Guid OrderId { get; init; }
    [Searchable(Backend = SearchBackend.FullText, Group = "content", Weight = 1.0f)]
    public string Body { get; init; } = "";
}

// Owner whose default-Inline "content" group pulls text from a related table via a
// 1:N join. The joined-table change-tracking trigger INSERTs into ferret_reindex_jobs,
// so the migration pipeline must ensure that table even though the group is Inline.
[SearchableEntity(Table = "ft_jorders")]
[SearchGroup("content", FullTextConfig = "english")]
public sealed class FtJoinedOrder : ISearchableEntity<Guid>
{
    public Guid Id { get; init; }

    [Searchable(Backend = SearchBackend.FullText, Group = "content", Weight = 2.0f)]
    public string Title { get; init; } = "";

    [SearchJoin(ForeignKey = "order_id")]
    public IReadOnlyList<FtReview> Reviews { get; init; } = [];
}

public sealed class FullTextMigDbContext : DbContext
{
    public FullTextMigDbContext(DbContextOptions<FullTextMigDbContext> opts) : base(opts) { }
    public DbSet<FullTextProduct> Products => Set<FullTextProduct>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<FullTextProduct>(e =>
        {
            e.ToTable("ft_products");
            e.HasKey(p => p.Id);
            e.Property(p => p.Name).IsRequired();
            e.Property(p => p.Description).IsRequired();
        });
        modelBuilder.UseFerretSearchableAnnotations(typeof(FullTextProduct).Assembly);
    }
}

public sealed class FtJoinedMigDbContext : DbContext
{
    public FtJoinedMigDbContext(DbContextOptions<FtJoinedMigDbContext> opts) : base(opts) { }
    public DbSet<FtJoinedOrder> Orders => Set<FtJoinedOrder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<FtJoinedOrder>(e =>
        {
            e.ToTable("ft_jorders");
            e.HasKey(o => o.Id);
            e.Property(o => o.Title).IsRequired();
            e.Ignore(o => o.Reviews);
        });
        modelBuilder.UseFerretSearchableAnnotations(typeof(FtJoinedOrder).Assembly);
    }
}

[Collection("postgres")]
public class FullTextMigrationEndToEndTests
{
    private readonly PostgresFixture _fx;

    public FullTextMigrationEndToEndTests(PostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task Diff_creates_sidecar_trigger_and_index_then_DDL_applies_and_indexes_rows()
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();

        await conn.ExecuteAsync(@"
            DROP TABLE IF EXISTS ft_products_search CASCADE;
            DROP TABLE IF EXISTS ft_products CASCADE;
            DROP FUNCTION IF EXISTS ft_products_search_sync();
            CREATE TABLE ft_products (
                id uuid PRIMARY KEY,
                name text NOT NULL,
                description text NOT NULL
            );");

        var dbOpts = new DbContextOptionsBuilder<FullTextMigDbContext>()
            .UseNpgsql(_fx.ConnectionString)
            .EnableServiceProviderCaching(false)
            .Options;
        await using var ctx = new FullTextMigDbContext(dbOpts);

        var designServices = new ServiceCollection();
        designServices.AddEntityFrameworkDesignTimeServices();
        designServices.AddDbContextDesignTimeServices(ctx);
        new MigDesignTimeServices().ConfigureDesignTimeServices(designServices);
        var designProvider = designServices.BuildServiceProvider();

        var differ = designProvider.GetRequiredService<IMigrationsModelDiffer>();
        var ops = differ.GetDifferences(
            source: null,
            target: ctx.GetService<IDesignTimeModel>().Model.GetRelationalModel());

        ops.OfType<EnsureSidecarTableOperation>().Should().HaveCount(1);
        ops.OfType<CreateFullTextGroupOperation>().Should().HaveCount(1)
            .And.Subject.Single().Group.Name.Should().Be("content");
        ops.OfType<EnsureReindexJobsTableOperation>().Should().BeEmpty();

        foreach (var op in ops)
        {
            string? sql = op switch
            {
                EnsureSidecarTableOperation s => FullTextDdlBuilder.CreateSidecarTable(
                    s.SidecarTable, s.SidecarSchema, s.SourceTable, s.SourceSchema, s.IdColumn, s.IdColumnType),
                CreateFullTextGroupOperation c => BuildCreateGroupSql(c),
                _ => null,
            };
            if (sql is null) continue;
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync();
        }

        var id = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO ft_products (id, name, description) VALUES (@Id, @Name, @Description)",
            new { Id = id, Name = "tractor", Description = "diesel farm equipment" });

        (await conn.QuerySingleAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM pg_tables WHERE tablename='ft_products_search')")).Should().BeTrue();
        (await conn.QuerySingleAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname='ft_products_search_sync_t' AND tgisinternal = false)")).Should().BeTrue();
        (await conn.QuerySingleAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM pg_indexes WHERE indexname='ix_ft_products_search_content_tsv_gin')")).Should().BeTrue();
        (await conn.QuerySingleAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM ft_products_search WHERE content_tsv @@ websearch_to_tsquery('english', 'tractor'))")).Should().BeTrue();
    }

    [Fact]
    public async Task Inline_joined_group_diff_ensures_jobs_table_so_related_write_enqueues()
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();

        await conn.ExecuteAsync(@"
            DROP TABLE IF EXISTS ft_jorders_search CASCADE;
            DROP TABLE IF EXISTS ft_reviews CASCADE;
            DROP TABLE IF EXISTS ft_jorders CASCADE;
            DROP TABLE IF EXISTS ferret_reindex_jobs CASCADE;
            DROP FUNCTION IF EXISTS ft_jorders_search_sync() CASCADE;
            DROP FUNCTION IF EXISTS ft_jorders__ft_reviews_ct() CASCADE;
            CREATE TABLE ft_jorders (
                id uuid PRIMARY KEY,
                title text NOT NULL
            );
            CREATE TABLE ft_reviews (
                id uuid PRIMARY KEY,
                order_id uuid NOT NULL REFERENCES ft_jorders(id) ON DELETE CASCADE,
                body text NOT NULL
            );");

        var dbOpts = new DbContextOptionsBuilder<FtJoinedMigDbContext>()
            .UseNpgsql(_fx.ConnectionString)
            .EnableServiceProviderCaching(false)
            .Options;
        await using var ctx = new FtJoinedMigDbContext(dbOpts);

        var designServices = new ServiceCollection();
        designServices.AddEntityFrameworkDesignTimeServices();
        designServices.AddDbContextDesignTimeServices(ctx);
        new MigDesignTimeServices().ConfigureDesignTimeServices(designServices);
        var designProvider = designServices.BuildServiceProvider();

        var differ = designProvider.GetRequiredService<IMigrationsModelDiffer>();
        var ops = differ.GetDifferences(
            source: null,
            target: ctx.GetService<IDesignTimeModel>().Model.GetRelationalModel()).ToList();

        // The group is default-Inline, yet a joined-table trigger is emitted, so the
        // jobs table MUST be ensured and ordered before the trigger that references it.
        var createGroup = ops.OfType<CreateFullTextGroupOperation>().Should().ContainSingle().Subject;
        createGroup.ReindexMode.Should().Be(ReindexMode.Inline);
        ops.OfType<CreateJoinedTableTriggerOperation>().Should().ContainSingle()
            .Which.JoinedTable.Should().Be("ft_reviews");
        ops.OfType<EnsureReindexJobsTableOperation>().Should().ContainSingle();
        ops.FindIndex(o => o is EnsureReindexJobsTableOperation)
            .Should().BeLessThan(ops.FindIndex(o => o is CreateJoinedTableTriggerOperation));

        foreach (var op in ops)
        {
            string? sql = op switch
            {
                EnsureReindexJobsTableOperation => FullTextDdlBuilder.EnsureReindexJobsTable(),
                EnsureSidecarTableOperation s => FullTextDdlBuilder.CreateSidecarTable(
                    s.SidecarTable, s.SidecarSchema, s.SourceTable, s.SourceSchema, s.IdColumn, s.IdColumnType),
                CreateFullTextGroupOperation c => BuildCreateGroupSql(c),
                CreateJoinedTableTriggerOperation j => FullTextDdlBuilder.CreateChangeTrackingFunctionAndTrigger(
                    j.JoinedTable, j.JoinedSchema, j.SourceTable, j.SourceSchema,
                    new[] { j.IdColumn }, j.JoinPath,
                    FullTextSidecarNaming.ChangeTrackingFunctionName(j.SourceTable, j.JoinedTable, j.JoinedSchema),
                    FullTextSidecarNaming.ChangeTrackingTriggerName(j.SourceTable, j.JoinedTable, j.JoinedSchema),
                    j.Entity, j.GroupName),
                _ => null,
            };
            if (sql is null) continue;
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync();
        }

        // Jobs table exists (would not without the fix → the INSERT below would fail).
        (await conn.QuerySingleAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM pg_tables WHERE tablename='ferret_reindex_jobs')")).Should().BeTrue();

        var orderId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO ft_jorders (id, title) VALUES (@Id, @Title)",
            new { Id = orderId, Title = "gizmo" });

        // A related-row write fires the change-tracking trigger, which enqueues the
        // owner into ferret_reindex_jobs. This is the runtime path the blocker broke.
        await conn.ExecuteAsync(
            "INSERT INTO ft_reviews (id, order_id, body) VALUES (@Id, @OrderId, @Body)",
            new { Id = Guid.NewGuid(), OrderId = orderId, Body = "great product" });

        (await conn.QuerySingleAsync<long>(
            "SELECT count(*) FROM ferret_reindex_jobs WHERE group_name = 'content' AND status = 'pending'"))
            .Should().BeGreaterThan(0);
    }

    private static string BuildCreateGroupSql(CreateFullTextGroupOperation c)
    {
        var column = c.Group.Name + c.ColumnSuffix;
        var sb = new System.Text.StringBuilder();
        sb.Append(FullTextDdlBuilder.AddGroupColumn(c.SidecarTable, c.SidecarSchema, column));
        sb.Append(FullTextDdlBuilder.CreateGroupIndex(c.SidecarTable, c.SidecarSchema,
            FullTextSidecarNaming.IndexName(c.SidecarTable, column), column));
        sb.Append(FullTextDdlBuilder.CreateSyncFunctionAndTrigger(
            c.SidecarTable, c.SidecarSchema, c.SourceTable, c.SourceSchema, c.IdColumn,
            FullTextSidecarNaming.SyncFunctionName(c.SourceTable),
            FullTextSidecarNaming.SyncTriggerName(c.SourceTable),
            c.ColumnSuffix, c.AllGroupsAfter));
        if (c.ReindexMode == ReindexMode.Inline)
        {
            sb.Append(FullTextDdlBuilder.Backfill(
                c.SidecarTable, c.SidecarSchema, c.SourceTable, c.SourceSchema,
                c.IdColumn, c.ColumnSuffix, c.AllGroupsAfter));
        }
        return sb.ToString();
    }
}
