using System.Text.Json;
using Ferret.Abstractions;
using Ferret.Core.Backends.FullText;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Ferret.Migrations.Tests.Annotations;

public class FullTextAnnotationWriterTests
{
    [SearchableEntity(Table = "products")]
    [SearchGroup("content", FullTextConfig = "english")]
    public sealed class Product : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }

        [Searchable(Backend = SearchBackend.FullText, Group = "content")]
        public string Name { get; init; } = "";

        [Searchable(Backend = SearchBackend.FullText, Group = "content")]
        public string Description { get; init; } = "";
    }

    private sealed class ProductContext : DbContext
    {
        public ProductContext(DbContextOptions<ProductContext> opts) : base(opts) { }
        public DbSet<Product> Products => Set<Product>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Product>().ToTable("products");
            var registry = EntityRegistry.Build([typeof(Product)], new SnakeCaseNamingStrategy());
            var backends = new ISearchBackend[]
            {
                new TrigramSearchBackend(new PostgresDialect(), new TrigramOptions()),
                new FullTextSearchBackend(new PostgresDialect(), new FullTextOptions()),
            };
            modelBuilder.UseFerretSearchableAnnotations(registry, backends, new FullTextOptions { DefaultConfig = "english" });
        }
    }

    private static IModel BuildModel()
    {
        var opts = new DbContextOptionsBuilder<ProductContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        using var ctx = new ProductContext(opts);
        return ctx.Model;
    }

    private static IModel BuildModelViaScan()
    {
        var opts = new DbContextOptionsBuilder<DbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        var mb = new ModelBuilder();
        mb.Entity<Product>().ToTable("products");
        mb.UseFerretSearchableAnnotations(typeof(Product).Assembly);
        return mb.FinalizeModel();
    }

    [Fact]
    public void Assembly_scan_overload_also_writes_FullTextGroups_annotation()
    {
        var model = BuildModelViaScan();
        var entity = model.FindEntityType(typeof(Product))!;

        var annotation = entity.FindAnnotation(SearchableAnnotationKeys.FullTextGroupsV1);
        annotation.Should().NotBeNull("assembly-scan overload must route through the 4-arg fulltext path");
    }

    [SearchableEntity(Table = "articles")]
    public sealed class Article : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }

        [Searchable(Backend = SearchBackend.FullText, Group = "body", PreviousGroup = "content")]
        public string Name { get; init; } = "";

        [Searchable(Backend = SearchBackend.FullText, Group = "body")]
        public string Description { get; init; } = "";
    }

    private sealed class ArticleContext : DbContext
    {
        public ArticleContext(DbContextOptions<ArticleContext> opts) : base(opts) { }
        public DbSet<Article> Articles => Set<Article>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Article>().ToTable("articles");
            var registry = EntityRegistry.Build([typeof(Article)], new SnakeCaseNamingStrategy());
            var backends = new ISearchBackend[]
            {
                new TrigramSearchBackend(new PostgresDialect(), new TrigramOptions()),
                new FullTextSearchBackend(new PostgresDialect(), new FullTextOptions()),
            };
            modelBuilder.UseFerretSearchableAnnotations(registry, backends, new FullTextOptions { DefaultConfig = "english" });
        }
    }

    private static IModel BuildArticleModel()
    {
        var opts = new DbContextOptionsBuilder<ArticleContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        using var ctx = new ArticleContext(opts);
        return ctx.Model;
    }

    private static FullTextEntityGroupsDto DeserializeGroups(IModel model, Type clrType)
    {
        var entity = model.FindEntityType(clrType)!;
        var json = (string)entity.FindAnnotation(SearchableAnnotationKeys.FullTextGroupsV1)!.Value!;
        return JsonSerializer.Deserialize<FullTextEntityGroupsDto>(json)!;
    }

    [Fact]
    public void PreviousGroup_is_written_onto_renamed_group_annotation()
    {
        var dto = DeserializeGroups(BuildArticleModel(), typeof(Article));

        var group = dto.Groups.Single(g => g.Name == "body");
        group.PreviousGroup.Should().Be("content");
    }

    [Fact]
    public void Groups_without_hint_have_null_PreviousGroup()
    {
        var dto = DeserializeGroups(BuildModel(), typeof(Product));

        var group = dto.Groups.Single(g => g.Name == "content");
        group.PreviousGroup.Should().BeNull();
    }

    [SearchableEntity(Table = "tenant_docs", KeyProperties = ["TenantId", "DocId"])]
    [SearchGroup("content", FullTextConfig = "english")]
    public sealed class TenantDoc
    {
        public Guid TenantId { get; init; }
        public long DocId { get; init; }

        [Searchable(Backend = SearchBackend.FullText, Group = "content")]
        public string Body { get; init; } = "";
    }

    private sealed class TenantDocContext : DbContext
    {
        public TenantDocContext(DbContextOptions<TenantDocContext> opts) : base(opts) { }
        public DbSet<TenantDoc> TenantDocs => Set<TenantDoc>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<TenantDoc>(b =>
            {
                b.ToTable("tenant_docs");
                b.HasKey(e => new { e.TenantId, e.DocId });
            });
            var registry = EntityRegistry.Build([typeof(TenantDoc)], new SnakeCaseNamingStrategy());
            var backends = new ISearchBackend[]
            {
                new TrigramSearchBackend(new PostgresDialect(), new TrigramOptions()),
                new FullTextSearchBackend(new PostgresDialect(), new FullTextOptions()),
            };
            modelBuilder.UseFerretSearchableAnnotations(registry, backends, new FullTextOptions { DefaultConfig = "english" });
        }
    }

    private static IModel BuildTenantDocModel()
    {
        var opts = new DbContextOptionsBuilder<TenantDocContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        using var ctx = new TenantDocContext(opts);
        return ctx.Model;
    }

    [Fact]
    public void CompositeKey_round_trips_key_parts()
    {
        var dto = DeserializeGroups(BuildTenantDocModel(), typeof(TenantDoc));

        dto.KeyParts.Select(k => k.ColumnName).Should().Equal("tenant_id", "doc_id");
        dto.KeyParts.Select(k => k.ColumnType).Should().Equal("uuid", "bigint");

        // Single-key facade preserved for first part.
        dto.IdColumn.Should().Be("tenant_id");
        dto.IdColumnType.Should().Be("uuid");
    }

    [Fact]
    public void Writes_fulltext_groups_annotation_on_entity_with_fulltext_groups()
    {
        var model = BuildModel();
        var entity = model.FindEntityType(typeof(Product))!;

        var annotation = entity.FindAnnotation(SearchableAnnotationKeys.FullTextGroupsV1);
        annotation.Should().NotBeNull("the entity has fulltext groups");

        var json = (string)annotation!.Value!;
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("SidecarTable").GetString().Should().Be("products_search");
        root.GetProperty("SourceTable").GetString().Should().Be("products");
        root.GetProperty("IdColumn").GetString().Should().Be("id");

        var groups = root.GetProperty("Groups");
        groups.GetArrayLength().Should().Be(1);
        var group = groups[0];
        group.GetProperty("Name").GetString().Should().Be("content");
        group.GetProperty("FullTextConfig").GetString().Should().Be("english");

        var props = group.GetProperty("Properties");
        props.GetArrayLength().Should().Be(2);

        var propNames = Enumerable.Range(0, props.GetArrayLength())
            .Select(i => props[i].GetProperty("PropertyName").GetString())
            .ToHashSet();
        propNames.Should().BeEquivalentTo(["Name", "Description"]);
    }
}
