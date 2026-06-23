using System.Text.Json;
using Ferret.Abstractions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Ferret.Migrations.Tests.Handlers;

public class SearchableSnapshotHandlerTests
{
    public sealed class Widget
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = "";
    }

    private sealed class TestContext : DbContext
    {
        private static readonly SearchIndexDefinition Definition = new()
        {
            IndexName = "ix_widgets_name_gist_trgm",
            TableName = "widgets",
            ColumnName = "name",
            IndexSql = "CREATE INDEX ...",
            RequiredExtensions = ["pg_trgm"],
        };

        public TestContext(DbContextOptions<TestContext> opts) : base(opts) { }
        public DbSet<Widget> Widgets => Set<Widget>();
        protected override void OnModelCreating(ModelBuilder mb)
        {
            mb.Entity<Widget>(e =>
            {
                e.ToTable("widgets");
                e.Property(w => w.Name)
                    .HasAnnotation(SearchableAnnotationKeys.SearchableIndexV1, JsonSerializer.Serialize(Definition));
            });
            mb.Model.SetAnnotation(
                SearchableAnnotationKeys.RequiredExtensionsV1,
                JsonSerializer.Serialize(new[] { "pg_trgm" }));
        }
    }

    [Fact]
    public void Emits_HasAnnotation_calls_for_root_and_property_annotations()
    {
        var opts = new DbContextOptionsBuilder<TestContext>()
            .UseNpgsql("Host=localhost;Database=ferret_test")
            .EnableServiceProviderCaching(false)
            .Options;
        using var ctx = new TestContext(opts);
        var handler = new SearchableSnapshotHandler();
        var builder = new IndentedStringBuilder();

        handler.GenerateSnapshot(ctx.Model, builder);

        var output = builder.ToString();
        output.Should().Contain($"\"{SearchableAnnotationKeys.RequiredExtensionsV1}\"");
        output.Should().Contain($"\"{SearchableAnnotationKeys.SearchableIndexV1}\"");
        output.Should().Contain("modelBuilder.HasAnnotation(");
        output.Should().Contain(".Property<string>(\"Name\")");
    }
}
