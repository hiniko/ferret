using Ferret.Abstractions.Attributes;
using Ferret.Abstractions.Search;
using Ferret.Core.Backends.FullText;
using FluentAssertions;
using Xunit;

namespace Ferret.Core.Tests;

public class FullTextBackfillBatchTests
{
    private sealed class Author { }

    private static FullTextGroup[] JoinedGroup() =>
    [
        new FullTextGroup
        {
            Name = "content", FullTextConfig = "english", Reindex = ReindexMode.Concurrent,
            Properties =
            [
                new FullTextGroupProperty { PropertyName = "Title", ColumnName = "title", Weight = FullTextWeightBucket.A },
                new FullTextGroupProperty
                {
                    PropertyName = "AuthorNames", ColumnName = "name", Weight = FullTextWeightBucket.B,
                    Join = new JoinPath
                    {
                        Hops =
                        [
                            new JoinHop
                            {
                                TableName = "authors", TableAlias = "j_authors",
                                ForeignKeyColumn = "doc_id", EntityType = typeof(Author),
                                Cardinality = JoinCardinality.OneToMany, ForeignKeyOwningSide = false,
                            },
                        ],
                    },
                },
            ],
        },
    ];
    private static FullTextGroup[] SingleGroup() =>
    [
        new FullTextGroup
        {
            Name = "content", FullTextConfig = "english", Reindex = ReindexMode.Inline,
            Properties =
            [
                new FullTextGroupProperty { PropertyName = "Title", ColumnName = "title", Weight = FullTextWeightBucket.A },
                new FullTextGroupProperty { PropertyName = "Body",  ColumnName = "body",  Weight = FullTextWeightBucket.B },
            ],
        },
    ];

    private static FullTextGroup[] MultiGroup() =>
    [
        new FullTextGroup
        {
            Name = "content", FullTextConfig = "english", Reindex = ReindexMode.Inline,
            Properties =
            [
                new FullTextGroupProperty { PropertyName = "Title", ColumnName = "title", Weight = FullTextWeightBucket.A },
                new FullTextGroupProperty { PropertyName = "Body",  ColumnName = "body",  Weight = FullTextWeightBucket.B },
            ],
        },
        new FullTextGroup
        {
            Name = "tags", FullTextConfig = "simple", Reindex = ReindexMode.Inline,
            Properties =
            [
                new FullTextGroupProperty { PropertyName = "Tags", ColumnName = "tags", Weight = FullTextWeightBucket.A },
            ],
        },
    ];

    [Fact]
    public void BackfillBatch_numeric_key_emits_keyset_chunk()
    {
        var groups = SingleGroup();

        var sql = FullTextDdlBuilder.BackfillBatch(
            sidecarTable: "docs_search", sidecarSchema: null,
            sourceTable:  "docs",        sourceSchema:  null,
            idColumn: "id", columnSuffix: "_tsv", groups: groups, greaterThan: true);

        sql.Should().Contain("INSERT INTO \"docs_search\" (\"id\", \"content_tsv\", \"updated_at\")");
        sql.Should().Contain("FROM \"docs\"");
        sql.Should().Contain("WHERE \"id\" > @last_id");
        sql.Should().Contain("ORDER BY \"id\"");
        sql.Should().Contain("LIMIT @batch_size");
        sql.Should().Contain("ON CONFLICT (\"id\") DO UPDATE SET");
        sql.Should().Contain("\"content_tsv\" = EXCLUDED.\"content_tsv\"");
        sql.Should().Contain("RETURNING \"id\"");
        sql.Should().Contain("SELECT count(*), (SELECT \"id\" FROM \"_indexed\" ORDER BY \"id\" DESC LIMIT 1) FROM \"_indexed\";");
    }

    [Fact]
    public void BackfillBatch_seed_variant_uses_greater_or_equal()
    {
        var sql = FullTextDdlBuilder.BackfillBatch(
            sidecarTable: "docs_search", sidecarSchema: null,
            sourceTable:  "docs",        sourceSchema:  null,
            idColumn: "id", columnSuffix: "_tsv", groups: SingleGroup(),
            greaterThan: false);

        sql.Should().Contain("WHERE \"id\" >= @last_id");
        sql.Should().NotContain("WHERE \"id\" > @last_id");
    }

    [Fact]
    public void BackfillBatch_uuid_text_key_emits_keyset_chunk()
    {
        var groups = SingleGroup();

        var sql = FullTextDdlBuilder.BackfillBatch(
            sidecarTable: "docs_search", sidecarSchema: null,
            sourceTable:  "docs",        sourceSchema:  null,
            idColumn: "slug", columnSuffix: "_tsv", groups: groups, greaterThan: true);

        sql.Should().Contain("WHERE \"slug\" > @last_id");
        sql.Should().Contain("ORDER BY \"slug\"");
        sql.Should().Contain("LIMIT @batch_size");
        sql.Should().Contain("RETURNING \"slug\"");
        sql.Should().Contain("SELECT count(*), (SELECT \"slug\" FROM \"_indexed\" ORDER BY \"slug\" DESC LIMIT 1) FROM \"_indexed\";");
    }

    [Fact]
    public void BackfillBatch_multi_group_setweight_matches_backfill()
    {
        var groups = MultiGroup();

        var batch = FullTextDdlBuilder.BackfillBatch(
            sidecarTable: "docs_search", sidecarSchema: null,
            sourceTable:  "docs",        sourceSchema:  null,
            idColumn: "id", columnSuffix: "_tsv", groups: groups, greaterThan: true);

        var backfill = FullTextDdlBuilder.Backfill(
            sidecarTable: "docs_search", sidecarSchema: null,
            sourceTable:  "docs",        sourceSchema:  null,
            idColumn: "id", columnSuffix: "_tsv", groups: groups);

        // setweight expressions must be byte-identical to Backfill for the same groups
        batch.Should().Contain("setweight(to_tsvector('english', coalesce(\"title\", '')), 'A')");
        batch.Should().Contain("setweight(to_tsvector('english', coalesce(\"body\", '')), 'B')");
        batch.Should().Contain("setweight(to_tsvector('simple', coalesce(\"tags\", '')), 'A')");

        foreach (var expr in new[]
        {
            "setweight(to_tsvector('english', coalesce(\"title\", '')), 'A')",
            "setweight(to_tsvector('english', coalesce(\"body\", '')), 'B')",
            "setweight(to_tsvector('simple', coalesce(\"tags\", '')), 'A')",
        })
        {
            backfill.Should().Contain(expr);
        }

        batch.Should().Contain("WHERE \"id\" > @last_id");
        batch.Should().Contain("ORDER BY \"id\"");
        batch.Should().Contain("LIMIT @batch_size");
        batch.Should().Contain("RETURNING \"id\"");
        batch.Should().Contain("SELECT count(*), (SELECT \"id\" FROM \"_indexed\" ORDER BY \"id\" DESC LIMIT 1) FROM \"_indexed\";");
    }

    [Fact]
    public void BackfillBatch_joined_group_uses_join_aggregate_filtered_to_owner_range()
    {
        var groups = JoinedGroup();

        var sql = FullTextDdlBuilder.BackfillBatch(
            sidecarTable: "docs_search", sidecarSchema: null,
            sourceTable:  "docs",        sourceSchema:  null,
            idColumn: "id", columnSuffix: "_tsv", groups: groups, greaterThan: true);

        // The keyset chunk is still computed over the owner key range.
        sql.Should().Contain("WHERE \"id\" > @last_id");
        sql.Should().Contain("ORDER BY \"id\"");
        sql.Should().Contain("LIMIT @batch_size");

        // Joined groups must LEFT JOIN the related table aliased off the owner.
        sql.Should().Contain("FROM \"docs\" \"e\"");
        sql.Should().Contain("LEFT JOIN \"authors\" \"j_authors\" ON");

        // OneToMany columns are aggregated; owner-local columns stay qualified.
        sql.Should().Contain("string_agg(\"j_authors\".\"name\", ' ')");
        sql.Should().Contain("setweight(to_tsvector('english', coalesce(\"e\".\"title\", '')), 'A')");

        // Aggregation must group by the owner key and be filtered to the batch range.
        sql.Should().Contain("WHERE \"e\".\"id\" IN (SELECT \"id\" FROM \"_batch\")");
        sql.Should().Contain("GROUP BY \"e\".\"id\"");

        sql.Should().Contain("RETURNING \"id\"");
        sql.Should().Contain("SELECT count(*), (SELECT \"id\" FROM \"_indexed\" ORDER BY \"id\" DESC LIMIT 1) FROM \"_indexed\";");
    }
}
