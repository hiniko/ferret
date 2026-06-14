using System.Text.Json;
using Ferret.Migrations.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Ferret.Migrations.Tests.Handlers;

internal static class VectorModelFixtures
{
    public sealed class VArticle
    {
        public Guid Id { get; init; }
        public string Title { get; init; } = "";
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
            .UseSqlite("DataSource=:memory:")
            .EnableServiceProviderCaching(false)
            .Options;
        using var ctx = new TestContext(configure, opts);
        return ctx.Model.GetRelationalModel();
    }

    public static IRelationalModel ModelWithGroup(int dims)
    {
        var dto = new VectorEntityGroupsDto
        {
            SidecarTable = "varticles_vec",
            SidecarSchema = null,
            SourceTable = "varticles",
            SourceSchema = null,
            IdColumn = "id",
            IdColumnType = "uuid",
            ColumnSuffix = "_vec",
            HnswM = 16,
            HnswEfConstruction = 64,
            Groups =
            [
                new VectorGroupDto
                {
                    Name = "content",
                    Dimensions = dims,
                    Properties =
                    [
                        new VectorGroupPropertyDto
                        {
                            PropertyName = "Title",
                            ColumnName = "title",
                            EmbeddingSource = "Title",
                        },
                    ],
                },
            ],
        };

        return BuildRelationalModel(mb =>
        {
            var entity = mb.Entity<VArticle>();
            entity.ToTable("varticles");
            entity.HasAnnotation(
                SearchableAnnotationKeys.VectorGroupsV1,
                JsonSerializer.Serialize(dto));
        });
    }

    public static IRelationalModel EmptyModel() => BuildRelationalModel(mb =>
    {
        mb.Entity<VArticle>().ToTable("varticles");
    });
}
