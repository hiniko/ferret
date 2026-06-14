using Ferret.Abstractions.Attributes;
using Ferret.Abstractions.Search;
using Ferret.Core.Backends.FullText;
using FluentAssertions;
using Xunit;

namespace Ferret.Core.Tests.Backends.FullText;

public class FullTextDdlBuilderTests
{
    private static FullTextOptions Opts() => new();

    [Fact]
    public void CreateSidecarTable_emits_pk_fk_and_updated_at()
    {
        var sql = FullTextDdlBuilder.CreateSidecarTable(
            sidecarTable: "products_search",
            sidecarSchema: null,
            sourceTable: "products",
            sourceSchema: null,
            idColumn: "id",
            idColumnType: "uuid");

        sql.Should().Contain("CREATE TABLE IF NOT EXISTS \"products_search\"");
        sql.Should().Contain("\"id\" uuid PRIMARY KEY");
        sql.Should().Contain("REFERENCES \"products\" (\"id\") ON DELETE CASCADE");
        sql.Should().Contain("\"updated_at\" timestamptz NOT NULL DEFAULT now()");
    }

    [Fact]
    public void CreateSidecarTable_with_schema_qualifies_both_tables()
    {
        var sql = FullTextDdlBuilder.CreateSidecarTable(
            sidecarTable: "products_search",
            sidecarSchema: "search",
            sourceTable: "products",
            sourceSchema: "app",
            idColumn: "id",
            idColumnType: "uuid");

        sql.Should().Contain("CREATE TABLE IF NOT EXISTS \"search\".\"products_search\"");
        sql.Should().Contain("REFERENCES \"app\".\"products\"");
    }

    [Fact]
    public void AddGroupColumn_emits_tsvector_column_and_gin_index()
    {
        var addCol = FullTextDdlBuilder.AddGroupColumn(
            sidecarTable: "products_search",
            sidecarSchema: null,
            groupColumn: "content_tsv");
        addCol.Should().Be(
            "ALTER TABLE \"products_search\" ADD COLUMN IF NOT EXISTS \"content_tsv\" tsvector;\n");

        var createIdx = FullTextDdlBuilder.CreateGroupIndex(
            sidecarTable: "products_search",
            sidecarSchema: null,
            indexName: "ix_products_search_content_tsv_gin",
            groupColumn: "content_tsv");
        createIdx.Should().Be(
            "CREATE INDEX IF NOT EXISTS \"ix_products_search_content_tsv_gin\" " +
            "ON \"products_search\" USING gin (\"content_tsv\");\n");
    }

    [Fact]
    public void DropGroupColumn_and_DropGroupIndex_emit_safe_drops()
    {
        FullTextDdlBuilder.DropGroupIndex(indexName: "ix_products_search_content_tsv_gin")
            .Should().Be("DROP INDEX IF EXISTS \"ix_products_search_content_tsv_gin\";\n");
        FullTextDdlBuilder.DropGroupColumn(
                sidecarTable: "products_search", sidecarSchema: null, groupColumn: "content_tsv")
            .Should().Be("ALTER TABLE \"products_search\" DROP COLUMN IF EXISTS \"content_tsv\";\n");
    }

    [Fact]
    public void CreateSyncFunctionAndTrigger_single_group_emits_valid_plpgsql()
    {
        var groups = new List<FullTextGroup>
        {
            new()
            {
                Name = "content",
                FullTextConfig = "english",
                Reindex = Ferret.Abstractions.Attributes.ReindexMode.Inline,
                Properties =
                [
                    new() { PropertyName = "Name",        ColumnName = "name",        Weight = FullTextWeightBucket.A },
                    new() { PropertyName = "Description", ColumnName = "description", Weight = FullTextWeightBucket.B },
                ],
            },
        };

        var sql = FullTextDdlBuilder.CreateSyncFunctionAndTrigger(
            sidecarTable: "products_search",
            sidecarSchema: null,
            sourceTable: "products",
            sourceSchema: null,
            idColumn: "id",
            functionName: "ferret_sync_products",
            triggerName: "ferret_trg_products",
            columnSuffix: "_tsv",
            groups: groups);

        // Function signature
        sql.Should().Contain("CREATE OR REPLACE FUNCTION \"ferret_sync_products\"()");
        // Body uses setweight + to_tsvector with coalesce
        sql.Should().Contain("setweight(to_tsvector('english', coalesce(NEW.\"name\", '')), 'A')");
        sql.Should().Contain("setweight(to_tsvector('english', coalesce(NEW.\"description\", '')), 'B')");
        // Upsert into sidecar
        sql.Should().Contain("INSERT INTO \"products_search\"");
        sql.Should().Contain("ON CONFLICT");
        // Trigger
        sql.Should().Contain("CREATE TRIGGER \"ferret_trg_products\"");
        sql.Should().Contain("AFTER INSERT OR UPDATE OF");
        sql.Should().Contain("ON \"products\"");
        // UPDATE OF columns are deterministically ordered (description before name alphabetically)
        var updateOfPos  = sql.IndexOf("UPDATE OF", StringComparison.Ordinal);
        var descPos      = sql.IndexOf("\"description\"", updateOfPos, StringComparison.Ordinal);
        var namePos      = sql.IndexOf("\"name\"", updateOfPos, StringComparison.Ordinal);
        descPos.Should().BeLessThan(namePos, "columns should be in ordinal order");
    }

    [Fact]
    public void DropSyncFunctionAndTrigger_emits_drop_trigger_then_function()
    {
        var sql = FullTextDdlBuilder.DropSyncFunctionAndTrigger(
            sourceTable: "products",
            sourceSchema: null,
            functionName: "ferret_sync_products",
            triggerName: "ferret_trg_products");

        sql.Should().Contain("DROP TRIGGER IF EXISTS \"ferret_trg_products\" ON \"products\"");
        sql.Should().Contain("DROP FUNCTION IF EXISTS \"ferret_sync_products\"()");
        // trigger drop must come before function drop
        sql.IndexOf("DROP TRIGGER", StringComparison.Ordinal)
           .Should().BeLessThan(sql.IndexOf("DROP FUNCTION", StringComparison.Ordinal));
    }

    [Fact]
    public void CreateSyncFunctionAndTrigger_multi_group_uses_per_group_config()
    {
        var groups = new[]
        {
            new FullTextGroup
            {
                Name = "content", FullTextConfig = "english", Reindex = Ferret.Abstractions.Attributes.ReindexMode.Inline,
                Properties = [ new FullTextGroupProperty { PropertyName = "Name", ColumnName = "name", Weight = FullTextWeightBucket.A } ],
            },
            new FullTextGroup
            {
                Name = "tags", FullTextConfig = "simple", Reindex = Ferret.Abstractions.Attributes.ReindexMode.Inline,
                Properties = [ new FullTextGroupProperty { PropertyName = "Tags", ColumnName = "tags", Weight = FullTextWeightBucket.A } ],
            },
        };

        var sql = FullTextDdlBuilder.CreateSyncFunctionAndTrigger(
            "products_search", null, "products", null, "id",
            "products_search_sync", "products_search_sync_t", "_tsv", groups);

        sql.Should().Contain("to_tsvector('english', coalesce(NEW.\"name\", ''))");
        sql.Should().Contain("to_tsvector('simple', coalesce(NEW.\"tags\", ''))");
        sql.Should().Contain("AFTER INSERT OR UPDATE OF \"name\", \"tags\"");
    }

    [Fact]
    public void CreateSyncFunctionAndTrigger_with_empty_groups_drops_function_and_trigger()
    {
        var sql = FullTextDdlBuilder.CreateSyncFunctionAndTrigger(
            "products_search", null, "products", null, "id",
            "products_search_sync", "products_search_sync_t", "_tsv", Array.Empty<FullTextGroup>());

        sql.Should().Contain("DROP TRIGGER IF EXISTS \"products_search_sync_t\"");
        sql.Should().Contain("DROP FUNCTION IF EXISTS \"products_search_sync\"");
    }

    [Fact]
    public void BuildBackfill_emits_insert_select_on_conflict()
    {
        var groups = new[]
        {
            new FullTextGroup
            {
                Name = "content", FullTextConfig = "english", Reindex = ReindexMode.Inline,
                Properties = new[]
                {
                    new FullTextGroupProperty { PropertyName = "Title", ColumnName = "title", Weight = FullTextWeightBucket.A },
                    new FullTextGroupProperty { PropertyName = "Body",  ColumnName = "body",  Weight = FullTextWeightBucket.B },
                },
            },
        };

        var sql = FullTextDdlBuilder.Backfill(
            sidecarTable: "docs_search", sidecarSchema: null,
            sourceTable:  "docs",        sourceSchema:  null,
            idColumn: "id", columnSuffix: "_tsv", groups: groups);

        sql.Should().Contain("INSERT INTO \"docs_search\" (\"id\", \"content_tsv\", \"updated_at\")");
        sql.Should().Contain("SELECT \"id\",");
        sql.Should().Contain("setweight(to_tsvector('english', coalesce(\"title\", '')), 'A')");
        sql.Should().Contain("FROM \"docs\"");
        sql.Should().Contain("ON CONFLICT (\"id\") DO UPDATE SET");
        sql.Should().Contain("\"content_tsv\" = EXCLUDED.\"content_tsv\"");
    }

    [Fact]
    public void Backfill_joined_group_emits_left_joins_string_agg_and_group_by()
    {
        var group = new FullTextGroup
        {
            Name = "content", FullTextConfig = "english", Reindex = ReindexMode.Inline,
            Properties =
            [
                new FullTextGroupProperty
                {
                    PropertyName = "Title", ColumnName = "title", Weight = FullTextWeightBucket.A,
                },
                new FullTextGroupProperty
                {
                    PropertyName = "CategoryName", ColumnName = "name", Weight = FullTextWeightBucket.B,
                    Join = new JoinPath
                    {
                        Hops =
                        [
                            new JoinHop
                            {
                                TableName = "categories", TableAlias = "cat1",
                                ForeignKeyColumn = "category_id", EntityType = typeof(object),
                                Cardinality = JoinCardinality.ManyToOne, ForeignKeyOwningSide = true,
                                Schema = "catalog",
                            },
                        ],
                    },
                },
                new FullTextGroupProperty
                {
                    PropertyName = "TagName", ColumnName = "tag", Weight = FullTextWeightBucket.C,
                    Join = new JoinPath
                    {
                        Hops =
                        [
                            new JoinHop
                            {
                                TableName = "tags", TableAlias = "tag1",
                                ForeignKeyColumn = "doc_id", EntityType = typeof(object),
                                Cardinality = JoinCardinality.OneToMany, ForeignKeyOwningSide = false,
                                Schema = "tagging",
                            },
                        ],
                    },
                },
            ],
        };

        var sql = FullTextDdlBuilder.Backfill(
            sidecarTable: "docs_search", sidecarSchema: null,
            sourceTable:  "docs",        sourceSchema:  null,
            idColumn: "id", columnSuffix: "_tsv", groups: new[] { group });

        // Owner table aliased
        sql.Should().Contain("FROM \"docs\" \"e\"");
        // Owner-local column qualified by owner alias
        sql.Should().Contain("setweight(to_tsvector('english', coalesce(\"e\".\"title\", '')), 'A')");
        // N:1 scalar qualified by join alias; aggregated with min() so it is valid
        // under the GROUP BY, but never string_agg'd (single-valued per owner)
        sql.Should().Contain("setweight(to_tsvector('english', coalesce(min(\"cat1\".\"name\"), '')), 'B')");
        // 1:N aggregated via string_agg
        sql.Should().Contain(
            "setweight(to_tsvector('english', coalesce(string_agg(\"tag1\".\"tag\", ' '), '')), 'C')");
        sql.Should().NotContain("string_agg(\"cat1\"");
        sql.Should().NotContain("string_agg(\"e\"");
        // LEFT JOINs with correct on-clauses, cross-schema qualified
        sql.Should().Contain(
            "LEFT JOIN \"catalog\".\"categories\" \"cat1\" ON \"cat1\".\"id\" = \"e\".\"category_id\"");
        sql.Should().Contain(
            "LEFT JOIN \"tagging\".\"tags\" \"tag1\" ON \"tag1\".\"doc_id\" = \"e\".\"id\"");
        // GROUP BY on the owner key, qualified by owner alias
        sql.Should().Contain("GROUP BY \"e\".\"id\"");
    }

    [Fact]
    public void Backfill_owner_local_only_sql_is_unchanged_when_joins_present_elsewhere()
    {
        var groups = new[]
        {
            new FullTextGroup
            {
                Name = "content", FullTextConfig = "english", Reindex = ReindexMode.Inline,
                Properties = new[]
                {
                    new FullTextGroupProperty { PropertyName = "Title", ColumnName = "title", Weight = FullTextWeightBucket.A },
                    new FullTextGroupProperty { PropertyName = "Body",  ColumnName = "body",  Weight = FullTextWeightBucket.B },
                },
            },
        };

        var sql = FullTextDdlBuilder.Backfill(
            sidecarTable: "docs_search", sidecarSchema: null,
            sourceTable:  "docs",        sourceSchema:  null,
            idColumn: "id", columnSuffix: "_tsv", groups: groups);

        // Regression: no joins, no alias, no GROUP BY for an owner-local-only group
        sql.Should().Contain("setweight(to_tsvector('english', coalesce(\"title\", '')), 'A')");
        sql.Should().Contain("FROM \"docs\"");
        sql.Should().NotContain("FROM \"docs\" \"e\"");
        sql.Should().NotContain("LEFT JOIN");
        sql.Should().NotContain("GROUP BY");
    }

    [Fact]
    public void CreateSidecarTable_composite_emits_pk_and_fk_tuple()
    {
        var sql = FullTextDdlBuilder.CreateSidecarTable(
            sidecarTable: "lineitems_search",
            sidecarSchema: null,
            sourceTable: "lineitems",
            sourceSchema: null,
            keyParts: new[] { ("order_id", "uuid"), ("line_no", "int") });

        // Two FK columns declared
        sql.Should().Contain("\"order_id\" uuid NOT NULL");
        sql.Should().Contain("\"line_no\" int NOT NULL");
        // Composite primary key
        sql.Should().Contain("PRIMARY KEY (\"order_id\", \"line_no\")");
        // Composite foreign key
        sql.Should().Contain(
            "FOREIGN KEY (\"order_id\", \"line_no\") REFERENCES \"lineitems\" (\"order_id\", \"line_no\") ON DELETE CASCADE");
        sql.Should().Contain("\"updated_at\" timestamptz NOT NULL DEFAULT now()");
    }

    [Fact]
    public void Sync_and_backfill_on_conflict_lists_all_key_columns()
    {
        var groups = new[]
        {
            new FullTextGroup
            {
                Name = "content", FullTextConfig = "english", Reindex = ReindexMode.Inline,
                Properties = [ new FullTextGroupProperty { PropertyName = "Title", ColumnName = "title", Weight = FullTextWeightBucket.A } ],
            },
        };
        var keyColumns = new[] { "order_id", "line_no" };

        var syncSql = FullTextDdlBuilder.CreateSyncFunctionAndTrigger(
            "lineitems_search", null, "lineitems", null, keyColumns,
            "lineitems_search_sync", "lineitems_search_sync_t", "_tsv", groups);
        syncSql.Should().Contain("ON CONFLICT (\"order_id\", \"line_no\") DO UPDATE SET");
        syncSql.Should().Contain("NEW.\"order_id\"");
        syncSql.Should().Contain("NEW.\"line_no\"");

        var backfillSql = FullTextDdlBuilder.Backfill(
            "lineitems_search", null, "lineitems", null, keyColumns, "_tsv", groups);
        backfillSql.Should().Contain("ON CONFLICT (\"order_id\", \"line_no\") DO UPDATE SET");

        var batchSql = FullTextDdlBuilder.BackfillBatch(
            "lineitems_search", null, "lineitems", null, keyColumns, "_tsv", groups, greaterThan: true);
        batchSql.Should().Contain("ON CONFLICT (\"order_id\", \"line_no\") DO UPDATE SET");
        // The keyset cursor must advance via a row-value comparison over ALL key
        // columns with one parameter per part — not a single-column scan.
        batchSql.Should().Contain(
            "WHERE (\"order_id\", \"line_no\") > (@last_id0, @last_id1)");

        var seedSql = FullTextDdlBuilder.BackfillBatch(
            "lineitems_search", null, "lineitems", null, keyColumns, "_tsv", groups, greaterThan: false);
        seedSql.Should().Contain(
            "WHERE (\"order_id\", \"line_no\") >= (@last_id0, @last_id1)");
    }

    [Fact]
    public void SingleKey_ddl_unchanged()
    {
        var groups = new[]
        {
            new FullTextGroup
            {
                Name = "content", FullTextConfig = "english", Reindex = ReindexMode.Inline,
                Properties = new[]
                {
                    new FullTextGroupProperty { PropertyName = "Title", ColumnName = "title", Weight = FullTextWeightBucket.A },
                    new FullTextGroupProperty { PropertyName = "Body",  ColumnName = "body",  Weight = FullTextWeightBucket.B },
                },
            },
        };

        var sidecarSingle = FullTextDdlBuilder.CreateSidecarTable(
            "products_search", null, "products", null, "id", "uuid");
        var sidecarList = FullTextDdlBuilder.CreateSidecarTable(
            "products_search", null, "products", null, new[] { ("id", "uuid") });
        sidecarList.Should().Be(sidecarSingle);

        var syncSingle = FullTextDdlBuilder.CreateSyncFunctionAndTrigger(
            "products_search", null, "products", null, "id",
            "ferret_sync_products", "ferret_trg_products", "_tsv", groups);
        var syncList = FullTextDdlBuilder.CreateSyncFunctionAndTrigger(
            "products_search", null, "products", null, new[] { "id" },
            "ferret_sync_products", "ferret_trg_products", "_tsv", groups);
        syncList.Should().Be(syncSingle);

        var backfillSingle = FullTextDdlBuilder.Backfill(
            "products_search", null, "products", null, "id", "_tsv", groups);
        var backfillList = FullTextDdlBuilder.Backfill(
            "products_search", null, "products", null, new[] { "id" }, "_tsv", groups);
        backfillList.Should().Be(backfillSingle);

        var batchSingle = FullTextDdlBuilder.BackfillBatch(
            "products_search", null, "products", null, "id", "_tsv", groups, greaterThan: true);
        var batchList = FullTextDdlBuilder.BackfillBatch(
            "products_search", null, "products", null, new[] { "id" }, "_tsv", groups, greaterThan: true);
        batchList.Should().Be(batchSingle);
    }

    [Fact]
    public void EnsureReindexJobsTable_is_idempotent()
    {
        var sql = FullTextDdlBuilder.EnsureReindexJobsTable();
        sql.Should().Contain("CREATE TABLE IF NOT EXISTS \"ferret_reindex_jobs\"");
        sql.Should().Contain("\"id\" bigserial PRIMARY KEY");
        sql.Should().Contain("\"entity\" text NOT NULL");
        sql.Should().Contain("\"group_name\" text NOT NULL");
        sql.Should().Contain("\"status\" text NOT NULL");
        sql.Should().Contain("\"batch_size\" int NOT NULL");
        sql.Should().Contain("\"last_id\" text");
        sql.Should().Contain("\"enqueued_at\" timestamptz NOT NULL DEFAULT now()");
    }

    [Fact]
    public void EnqueueReindexJob_emits_insert_with_pending_status()
    {
        var sql = FullTextDdlBuilder.EnqueueReindexJob(entity: "Product", group: "content", batchSize: 5000);
        sql.Should().Be(
            "INSERT INTO \"ferret_reindex_jobs\" (\"entity\", \"group_name\", \"status\", \"batch_size\") " +
            "VALUES ('Product', 'content', 'pending', 5000);\n");
    }
}
