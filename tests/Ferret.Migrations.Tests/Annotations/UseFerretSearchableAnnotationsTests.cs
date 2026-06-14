using System.Text.Json;
using Ferret.Abstractions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Ferret.Migrations.Tests.Annotations;

public class UseFerretSearchableAnnotationsTests
{
    [SearchableEntity(Table = "widgets")]
    public sealed class Widget : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }
        [Searchable] public string Name { get; init; } = "";
        [Searchable] public string Sku { get; init; } = "";
    }

    private sealed class WidgetContext : DbContext
    {
        public WidgetContext(DbContextOptions<WidgetContext> opts) : base(opts) { }
        public DbSet<Widget> Widgets => Set<Widget>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            var registry = EntityRegistry.Build([typeof(Widget)], new SnakeCaseNamingStrategy());
            var backends = new ISearchBackend[]
            {
                new TrigramSearchBackend(new PostgresDialect(), new TrigramOptions()),
            };
            modelBuilder.UseFerretSearchableAnnotations(registry, backends);
        }
    }

    private static IModel BuildModel()
    {
        var opts = new DbContextOptionsBuilder<WidgetContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        using var ctx = new WidgetContext(opts);
        return ctx.Model;
    }

    [Fact]
    public void Writes_searchable_index_annotation_per_searchable_property()
    {
        var model = BuildModel();
        var entity = model.FindEntityType(typeof(Widget))!;

        foreach (var name in new[] { "Name", "Sku" })
        {
            var prop = entity.FindProperty(name)!;
            var annotation = prop.FindAnnotation(SearchableAnnotationKeys.SearchableIndexV1);
            annotation.Should().NotBeNull($"property {name} should carry the searchable annotation");
            annotation!.Value.Should().BeOfType<string>();
            var json = (string)annotation.Value!;
            json.Should().Contain("\"IndexSql\"").And.Contain("gist_trgm_ops");
        }
    }

    [Fact]
    public void Aggregates_required_extensions_at_model_root()
    {
        var model = BuildModel();
        var annotation = model.FindAnnotation(SearchableAnnotationKeys.RequiredExtensionsV1);
        annotation.Should().NotBeNull();
        var extensions = JsonSerializer.Deserialize<string[]>((string)annotation!.Value!);
        extensions.Should().BeEquivalentTo(["pg_trgm"]);
    }

    [Fact]
    public void Throws_when_no_backend_handles_a_searchable_property()
    {
        var registry = EntityRegistry.Build([typeof(Widget)], new SnakeCaseNamingStrategy());
        var modelBuilder = new ModelBuilder();
        modelBuilder.Entity<Widget>();

        Action act = () => modelBuilder.UseFerretSearchableAnnotations(
            registry,
            backends: Array.Empty<ISearchBackend>());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*no backend*");
    }

    private sealed class WidgetContextDefaultOverload : DbContext
    {
        public WidgetContextDefaultOverload(DbContextOptions<WidgetContextDefaultOverload> opts) : base(opts) { }
        public DbSet<Widget> Widgets => Set<Widget>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            var registry = EntityRegistry.Build([typeof(Widget)], new SnakeCaseNamingStrategy());
            modelBuilder.UseFerretSearchableAnnotations(registry);
        }
    }

    private static IModel BuildModelDefaultOverload()
    {
        var opts = new DbContextOptionsBuilder<WidgetContextDefaultOverload>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        using var ctx = new WidgetContextDefaultOverload(opts);
        return ctx.Model;
    }

    [Fact]
    public void Default_overload_uses_trigram_backend_with_default_options()
    {
        var model = BuildModelDefaultOverload();
        var annotation = model
            .FindEntityType(typeof(Widget))!
            .FindProperty("Name")!
            .FindAnnotation(SearchableAnnotationKeys.SearchableIndexV1);
        annotation.Should().NotBeNull();
        ((string)annotation!.Value!).Should().Contain("gist_trgm_ops");
    }

    private sealed class WidgetContextAssemblyOverload : DbContext
    {
        public WidgetContextAssemblyOverload(DbContextOptions<WidgetContextAssemblyOverload> opts) : base(opts) { }
        public DbSet<Widget> Widgets => Set<Widget>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.UseFerretSearchableAnnotations(typeof(Widget).Assembly);
        }
    }

    private static IModel BuildModelAssemblyOverload()
    {
        var opts = new DbContextOptionsBuilder<WidgetContextAssemblyOverload>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        using var ctx = new WidgetContextAssemblyOverload(opts);
        return ctx.Model;
    }

    [Fact]
    public void Assembly_scan_overload_discovers_FerretEntity_types()
    {
        var model = BuildModelAssemblyOverload();
        var annotation = model
            .FindEntityType(typeof(Widget))!
            .FindProperty("Name")!
            .FindAnnotation(SearchableAnnotationKeys.SearchableIndexV1);
        annotation.Should().NotBeNull();
    }
}
