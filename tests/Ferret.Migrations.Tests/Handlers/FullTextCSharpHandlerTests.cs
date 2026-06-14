using Ferret.Abstractions.Search;
using Ferret.Migrations.Operations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Ferret.Migrations.Tests.Handlers;

public class FullTextCSharpHandlerTests
{
    private static FullTextGroup Group(string name = "default") => new()
    {
        Name = name,
        FullTextConfig = "english",
        Reindex = ReindexMode.Inline,
        Properties = new[]
        {
            new FullTextGroupProperty
            {
                PropertyName = "Title",
                ColumnName = "title",
                Weight = FullTextWeightBucket.A,
                FullTextConfigOverride = null,
            },
        },
    };

    private static FullTextGroup Group(ReindexMode mode) => new()
    {
        Name = "content",
        FullTextConfig = "english",
        Reindex = mode,
        Properties = new[]
        {
            new FullTextGroupProperty
            {
                PropertyName = "Title",
                ColumnName = "title",
                Weight = FullTextWeightBucket.A,
                FullTextConfigOverride = null,
            },
        },
    };

    private static string Emit(EnsureSidecarTableOperation op)
    {
        var handler = new SearchableCSharpHandler();
        var builder = new IndentedStringBuilder();
        handler.Generate(op, builder);
        return builder.ToString();
    }

    private static string Emit(CreateFullTextGroupOperation op)
    {
        var handler = new SearchableCSharpHandler();
        var builder = new IndentedStringBuilder();
        handler.Generate(op, builder);
        return builder.ToString();
    }

    private static string Emit(DropFullTextGroupOperation op)
    {
        var handler = new SearchableCSharpHandler();
        var builder = new IndentedStringBuilder();
        handler.Generate(op, builder);
        return builder.ToString();
    }

    private static string Emit(CreateJoinedTableTriggerOperation op)
    {
        var handler = new SearchableCSharpHandler();
        var builder = new IndentedStringBuilder();
        handler.Generate(op, builder);
        return builder.ToString();
    }

    private static string Emit(DropJoinedTableTriggerOperation op)
    {
        var handler = new SearchableCSharpHandler();
        var builder = new IndentedStringBuilder();
        handler.Generate(op, builder);
        return builder.ToString();
    }

    private static JoinPath DirectOneToMany() => new()
    {
        Hops =
        [
            new JoinHop
            {
                TableName = "comments", TableAlias = "c1",
                ForeignKeyColumn = "article_id", EntityType = typeof(object),
                Cardinality = JoinCardinality.OneToMany, ForeignKeyOwningSide = false,
            },
        ],
    };

    private static CreateJoinedTableTriggerOperation CreateJoinedOp() => new()
    {
        Entity = "Article",
        SidecarTable = "articles_search", SidecarSchema = null,
        SourceTable = "articles", SourceSchema = null,
        IdColumn = "id",
        JoinedTable = "comments", JoinedSchema = null,
        GroupName = "content",
        JoinPath = DirectOneToMany(),
    };

    [Fact]
    public void CanHandle_returns_true_for_joined_table_trigger_operations()
    {
        var handler = new SearchableCSharpHandler();
        handler.CanHandle(CreateJoinedOp()).Should().BeTrue();
        handler.CanHandle(new DropJoinedTableTriggerOperation
        {
            Entity = "Article",
            SidecarTable = "articles_search", SidecarSchema = null,
            SourceTable = "articles", SourceSchema = null,
            IdColumn = "id",
            JoinedTable = "comments", JoinedSchema = null,
        }).Should().BeTrue();
    }

    [Fact]
    public void CreateJoinedTableTrigger_emits_enqueue_trigger_sql()
    {
        var output = Emit(CreateJoinedOp());
        output.Should().Contain("migrationBuilder.Sql(\"\"\"");
        output.Should().Contain("CREATE OR REPLACE FUNCTION \"articles__comments_ct\"");
        output.Should().Contain("CREATE TRIGGER \"articles__comments_ct_t\"");
        output.Should().Contain("ON \"comments\"");
        output.Should().Contain("AFTER INSERT OR UPDATE OR DELETE");
        output.Should().Contain("INSERT INTO \"ferret_reindex_jobs\"");
        output.Should().Contain("'Article'");
        output.Should().Contain("'content'");
    }

    [Fact]
    public void CreateJoinedTableTrigger_includes_joined_schema_in_names()
    {
        var op = new CreateJoinedTableTriggerOperation
        {
            Entity = "Article",
            SidecarTable = "articles_search", SidecarSchema = null,
            SourceTable = "articles", SourceSchema = null,
            IdColumn = "id",
            JoinedTable = "comments", JoinedSchema = "social",
            GroupName = "content",
            JoinPath = DirectOneToMany(),
        };

        var output = Emit(op);

        // schema is folded into the deterministic trigger/function name so two
        // same-named tables in different schemas do not collide.
        output.Should().Contain("CREATE OR REPLACE FUNCTION \"articles__social_comments_ct\"");
        output.Should().Contain("CREATE TRIGGER \"articles__social_comments_ct_t\"");
        output.Should().Contain("ON \"social\".\"comments\"");
    }

    [Fact]
    public void DropJoinedTableTrigger_emits_drop_trigger_and_function()
    {
        var output = Emit(new DropJoinedTableTriggerOperation
        {
            Entity = "Article",
            SidecarTable = "articles_search", SidecarSchema = null,
            SourceTable = "articles", SourceSchema = null,
            IdColumn = "id",
            JoinedTable = "comments", JoinedSchema = null,
        });
        output.Should().Contain("migrationBuilder.Sql(\"\"\"");
        output.Should().Contain("DROP TRIGGER IF EXISTS \"articles__comments_ct_t\" ON \"comments\"");
        output.Should().Contain("DROP FUNCTION IF EXISTS \"articles__comments_ct\"");
    }

    [Fact]
    public void EnsureSidecarTable_emits_sql_block()
    {
        var output = Emit(new EnsureSidecarTableOperation
        {
            SidecarTable = "products_search", SidecarSchema = null,
            SourceTable = "products", SourceSchema = null,
            IdColumn = "id", IdColumnType = "uuid",
        });
        output.Should().Contain("migrationBuilder.Sql(\"\"\"");
        output.Should().Contain("CREATE TABLE IF NOT EXISTS \"products_search\"");
    }

    [Fact]
    public void EnsureSidecarTable_composite_emits_multi_column_pk_and_fk()
    {
        var output = Emit(new EnsureSidecarTableOperation
        {
            SidecarTable = "tenant_docs_search", SidecarSchema = null,
            SourceTable = "tenant_docs", SourceSchema = null,
            IdColumn = "tenant_id", IdColumnType = "uuid",
            KeyParts =
            [
                new EnsureSidecarTableOperation.KeyPart { ColumnName = "tenant_id", ColumnType = "uuid" },
                new EnsureSidecarTableOperation.KeyPart { ColumnName = "id", ColumnType = "bigint" },
            ],
        });
        output.Should().Contain("PRIMARY KEY (\"tenant_id\", \"id\")");
        output.Should().Contain("FOREIGN KEY (\"tenant_id\", \"id\") REFERENCES \"tenant_docs\" (\"tenant_id\", \"id\")");
    }

    [Fact]
    public void Create_composite_emits_multi_column_on_conflict_and_backfill()
    {
        var g = Group(ReindexMode.Inline);
        var output = Emit(new CreateFullTextGroupOperation
        {
            Entity = "TenantDoc",
            SidecarTable = "tenant_docs_search", SidecarSchema = null,
            SourceTable = "tenant_docs", SourceSchema = null,
            IdColumn = "tenant_id",
            KeyColumns = ["tenant_id", "id"],
            ColumnSuffix = "_tsv",
            Group = g, AllGroupsAfter = new[] { g }, ReindexMode = ReindexMode.Inline,
        });
        output.Should().Contain("ON CONFLICT (\"tenant_id\", \"id\") DO UPDATE SET");
        output.Should().Contain("INSERT INTO \"tenant_docs_search\" (\"tenant_id\", \"id\"");
    }

    [Fact]
    public void Create_inline_emits_column_index_trigger_and_backfill()
    {
        var g = Group(ReindexMode.Inline);
        var output = Emit(new CreateFullTextGroupOperation
        {
            Entity = "Product",
            SidecarTable = "products_search", SidecarSchema = null,
            SourceTable = "products", SourceSchema = null,
            IdColumn = "id", ColumnSuffix = "_tsv",
            Group = g, AllGroupsAfter = new[] { g }, ReindexMode = ReindexMode.Inline,
        });
        output.Should().Contain("ADD COLUMN IF NOT EXISTS \"content_tsv\" tsvector");
        output.Should().Contain("CREATE INDEX IF NOT EXISTS \"ix_products_search_content_tsv_gin\"");
        output.Should().Contain("CREATE OR REPLACE FUNCTION \"products_search_sync\"");
        output.Should().Contain("INSERT INTO \"products_search\"");
        output.Should().Contain("FROM \"products\"");
        output.Should().NotContain("ferret_reindex_jobs");
    }

    [Fact]
    public void Create_concurrent_skips_backfill_and_enqueues_job()
    {
        var g = Group(ReindexMode.Concurrent);
        var output = Emit(new CreateFullTextGroupOperation
        {
            Entity = "Product",
            SidecarTable = "products_search", SidecarSchema = null,
            SourceTable = "products", SourceSchema = null,
            IdColumn = "id", ColumnSuffix = "_tsv",
            Group = g, AllGroupsAfter = new[] { g }, ReindexMode = ReindexMode.Concurrent,
            ConcurrentBatchSize = 5000,
        });
        output.Should().Contain("ADD COLUMN IF NOT EXISTS \"content_tsv\"");
        output.Should().Contain("ferret_reindex_jobs");
        output.Should().Contain("'Product'");
        // The Inline backfill INSERT...SELECT FROM "products" should NOT appear:
        output.Should().NotContain("FROM \"products\"\n");
    }

    [Fact]
    public void Create_deferred_skips_backfill_and_skips_enqueue()
    {
        var g = Group(ReindexMode.Deferred);
        var output = Emit(new CreateFullTextGroupOperation
        {
            Entity = "Product",
            SidecarTable = "products_search", SidecarSchema = null,
            SourceTable = "products", SourceSchema = null,
            IdColumn = "id", ColumnSuffix = "_tsv",
            Group = g, AllGroupsAfter = new[] { g }, ReindexMode = ReindexMode.Deferred,
        });
        output.Should().NotContain("FROM \"products\"");
        output.Should().NotContain("ferret_reindex_jobs");
    }

    [Fact]
    public void Drop_emits_index_column_drops_then_rebuilds_trigger()
    {
        var output = Emit(new DropFullTextGroupOperation
        {
            SidecarTable = "products_search", SidecarSchema = null,
            SourceTable = "products", SourceSchema = null,
            IdColumn = "id", ColumnSuffix = "_tsv",
            GroupName = "content", AllGroupsAfter = Array.Empty<FullTextGroup>(),
        });
        output.Should().Contain("DROP INDEX IF EXISTS \"ix_products_search_content_tsv_gin\"");
        output.Should().Contain("DROP COLUMN IF EXISTS \"content_tsv\"");
        output.Should().Contain("DROP TRIGGER IF EXISTS \"products_search_sync_t\"");
    }

    [Fact]
    public void Alter_rebuilds_trigger_and_backfills_when_inline()
    {
        var handler = new SearchableCSharpHandler();
        var builder = new IndentedStringBuilder();

        var group = Group();
        var op = new AlterFullTextGroupOperation
        {
            Entity = "articles",
            SidecarTable = "articles_search",
            SidecarSchema = null,
            SourceTable = "articles",
            SourceSchema = null,
            IdColumn = "id",
            ColumnSuffix = "_tsv",
            Group = group,
            AllGroupsAfter = new[] { group },
            ReindexMode = ReindexMode.Inline,
        };

        handler.Generate(op, builder);
        var output = builder.ToString();

        output.Should().Contain("CREATE OR REPLACE FUNCTION");
        output.Should().Contain("CREATE TRIGGER");
        output.Should().Contain("FROM \"articles\"");
        output.Should().NotContain("ADD COLUMN IF NOT EXISTS");
        output.Should().NotContain("CREATE INDEX IF NOT EXISTS");
    }
}
