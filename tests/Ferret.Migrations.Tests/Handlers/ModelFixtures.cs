using System.Text.Json;
using Ferret.Abstractions.Attributes;
using Ferret.Abstractions.Search;
using Ferret.Migrations.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Ferret.Migrations.Tests.Handlers;

internal static class ModelFixtures
{
    public sealed class Article
    {
        public Guid Id { get; init; }
        public string Title { get; init; } = "";
        public string Body { get; init; } = "";
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

    public static FullTextEntityGroupsDto OneGroup(
        string groupName = "default",
        string fullTextConfig = "english",
        ReindexMode reindex = ReindexMode.Inline) => new()
    {
        SidecarTable = "articles_search",
        SidecarSchema = null,
        SourceTable = "articles",
        SourceSchema = null,
        IdColumn = "id",
        IdColumnType = "uuid",
        ColumnSuffix = "_tsv",
        Groups =
        [
            new FullTextGroupDto
            {
                Name = groupName,
                FullTextConfig = fullTextConfig,
                Reindex = reindex,
                Properties =
                [
                    new FullTextGroupPropertyDto
                    {
                        PropertyName = "Title",
                        ColumnName = "title",
                        Weight = FullTextWeightBucket.A,
                        FullTextConfigOverride = null,
                    },
                ],
            },
        ],
    };

    public static FullTextEntityGroupsDto GroupWithJoinedTables(params string[] joinedTables)
    {
        var properties = new List<FullTextGroupPropertyDto>
        {
            new()
            {
                PropertyName = "Title",
                ColumnName = "title",
                Weight = FullTextWeightBucket.A,
                FullTextConfigOverride = null,
            },
        };

        foreach (var table in joinedTables)
        {
            properties.Add(new FullTextGroupPropertyDto
            {
                PropertyName = $"{table}_Text",
                ColumnName = "text",
                Weight = FullTextWeightBucket.B,
                FullTextConfigOverride = null,
                Join = new FullTextJoinPathDto
                {
                    Hops =
                    [
                        new FullTextJoinHopDto
                        {
                            TableName = table,
                            TableAlias = table,
                            ForeignKeyColumn = "article_id",
                            Schema = null,
                            Cardinality = JoinCardinality.OneToMany,
                            ForeignKeyOwningSide = false,
                        },
                    ],
                },
            });
        }

        return new FullTextEntityGroupsDto
        {
            SidecarTable = "articles_search",
            SidecarSchema = null,
            SourceTable = "articles",
            SourceSchema = null,
            IdColumn = "id",
            IdColumnType = "uuid",
            ColumnSuffix = "_tsv",
            Groups =
            [
                new FullTextGroupDto
                {
                    Name = "default",
                    FullTextConfig = "english",
                    Reindex = ReindexMode.Inline,
                    Properties = properties,
                },
            ],
        };
    }

    public static IRelationalModel ModelWithGroups(FullTextEntityGroupsDto? dto)
    {
        return BuildRelationalModel(mb =>
        {
            var entity = mb.Entity<Article>();
            entity.ToTable("articles");
            if (dto is not null)
            {
                entity.HasAnnotation(
                    SearchableAnnotationKeys.FullTextGroupsV1,
                    JsonSerializer.Serialize(dto));
            }
        });
    }

    public static IRelationalModel EmptyModel() => BuildRelationalModel(mb =>
    {
        mb.Entity<Article>().ToTable("articles");
    });
}
