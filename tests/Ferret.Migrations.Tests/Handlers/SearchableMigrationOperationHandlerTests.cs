using System.Text.Json;
using Ferret.Abstractions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Ferret.Migrations.Tests.Handlers;

public class SearchableMigrationOperationHandlerTests
{
    public sealed class Widget
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = "";
    }

    private sealed class TestContext : DbContext
    {
        private readonly Action<ModelBuilder> _configure;
        public TestContext(Action<ModelBuilder> configure, DbContextOptions<TestContext> opts)
            : base(opts) { _configure = configure; }
        protected override void OnModelCreating(ModelBuilder modelBuilder) => _configure(modelBuilder);
    }

    private static IRelationalModel BuildRelationalModel(Action<ModelBuilder> configure)
    {
        var opts = new DbContextOptionsBuilder<TestContext>()
            .UseNpgsql("Host=localhost;Database=ferret_test")
            .EnableServiceProviderCaching(false)
            .Options;
        using var ctx = new TestContext(configure, opts);
        return ctx.Model.GetRelationalModel();
    }

    private static SearchIndexDefinition NameIndex() => new()
    {
        IndexName = "ix_widgets_name_gist_trgm",
        TableName = "widgets",
        ColumnName = "name",
        IndexSql = "CREATE INDEX CONCURRENTLY IF NOT EXISTS \"ix_widgets_name_gist_trgm\" ON \"widgets\" USING gist ((\"name\"::text) gist_trgm_ops);",
        RequiredExtensions = ["pg_trgm"],
    };

    private static IRelationalModel ModelWithNameIndex(SearchIndexDefinition? definition)
    {
        return BuildRelationalModel(mb =>
        {
            var entity = mb.Entity<Widget>();
            entity.ToTable("widgets");
            if (definition is not null)
            {
                entity.Property(w => w.Name)
                    .HasAnnotation(SearchableAnnotationKeys.SearchableIndexV1, JsonSerializer.Serialize(definition));
                mb.Model.SetAnnotation(
                    SearchableAnnotationKeys.RequiredExtensionsV1,
                    JsonSerializer.Serialize(new[] { "pg_trgm" }));
            }
        });
    }

    [Fact]
    public void Add_searchable_emits_extension_then_create_op()
    {
        var source = ModelWithNameIndex(definition: null);
        var target = ModelWithNameIndex(NameIndex());

        var handler = new SearchableMigrationOperationHandler();
        var ops = handler.GetOperations(source, target, Array.Empty<MigrationOperation>());

        ops.Should().HaveCount(2);
        ops[0].Should().BeOfType<EnsurePgTrgmExtensionOperation>();
        var create = ops[1].Should().BeOfType<CreateSearchableIndexOperation>().Subject;
        create.IndexName.Should().Be("ix_widgets_name_gist_trgm");
        create.IndexSql.Should().Contain("gist_trgm_ops");
    }

    [Fact]
    public void Remove_searchable_emits_drop_op_only()
    {
        var source = ModelWithNameIndex(NameIndex());
        var target = ModelWithNameIndex(definition: null);

        var handler = new SearchableMigrationOperationHandler();
        var ops = handler.GetOperations(source, target, Array.Empty<MigrationOperation>());

        ops.Should().HaveCount(1);
        ops[0].Should().BeOfType<DropSearchableIndexOperation>()
            .Which.IndexName.Should().Be("ix_widgets_name_gist_trgm");
    }

    [Fact]
    public void Change_index_sql_emits_drop_then_create()
    {
        var source = ModelWithNameIndex(NameIndex());
        var changed = NameIndex() with { IndexSql = "CREATE INDEX … new SQL" };
        var target = ModelWithNameIndex(changed);

        var handler = new SearchableMigrationOperationHandler();
        var ops = handler.GetOperations(source, target, Array.Empty<MigrationOperation>());

        ops.Should().HaveCount(2);
        ops[0].Should().BeOfType<DropSearchableIndexOperation>();
        ops[1].Should().BeOfType<CreateSearchableIndexOperation>()
            .Which.IndexSql.Should().Be("CREATE INDEX … new SQL");
    }

    [Fact]
    public void HasDifferences_returns_true_when_annotations_differ()
    {
        var source = ModelWithNameIndex(definition: null);
        var target = ModelWithNameIndex(NameIndex());

        var handler = new SearchableMigrationOperationHandler();
        handler.HasDifferences(source, target, defaultHasDifferences: false).Should().BeTrue();
    }

    [Fact]
    public void HasDifferences_returns_false_when_annotations_match()
    {
        var source = ModelWithNameIndex(NameIndex());
        var target = ModelWithNameIndex(NameIndex());

        var handler = new SearchableMigrationOperationHandler();
        handler.HasDifferences(source, target, defaultHasDifferences: false).Should().BeFalse();
    }
}
