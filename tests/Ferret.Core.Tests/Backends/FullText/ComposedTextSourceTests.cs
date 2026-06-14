using Ferret.Abstractions.Search;
using Ferret.Core.Backends.FullText;
using FluentAssertions;
using Xunit;

namespace Ferret.Core.Tests.Backends.FullText;

public class ComposedTextSourceTests
{
    private static FullTextGroupProperty OwnerLocal(string column, FullTextWeightBucket weight) =>
        new()
        {
            PropertyName = column,
            ColumnName = column,
            Weight = weight,
            Join = null,
        };

    private static FullTextGroupProperty ManyToOne(string column, FullTextWeightBucket weight) =>
        new()
        {
            PropertyName = column,
            ColumnName = column,
            Weight = weight,
            Join = new JoinPath
            {
                Hops =
                [
                    new JoinHop
                    {
                        TableName = "categories",
                        TableAlias = "cat1",
                        ForeignKeyColumn = "category_id",
                        EntityType = typeof(object),
                        Cardinality = JoinCardinality.ManyToOne,
                        ForeignKeyOwningSide = true,
                        Schema = "catalog",
                    },
                ],
            },
        };

    private static FullTextGroupProperty OneToMany(string column, FullTextWeightBucket weight) =>
        new()
        {
            PropertyName = column,
            ColumnName = column,
            Weight = weight,
            Join = new JoinPath
            {
                Hops =
                [
                    new JoinHop
                    {
                        TableName = "tags",
                        TableAlias = "tag1",
                        ForeignKeyColumn = "product_id",
                        EntityType = typeof(object),
                        Cardinality = JoinCardinality.OneToMany,
                        ForeignKeyOwningSide = false,
                        Schema = "tagging",
                    },
                ],
            },
        };

    private static FullTextGroup MixedGroup() => new()
    {
        Name = "default",
        FullTextConfig = "english",
        Reindex = Abstractions.Attributes.ReindexMode.Inline,
        Properties =
        [
            OwnerLocal("title", FullTextWeightBucket.A),
            ManyToOne("category_name", FullTextWeightBucket.B),
            OneToMany("tag_name", FullTextWeightBucket.C),
        ],
    };

    [Fact]
    public void Build_mixed_group_classifies_each_property()
    {
        var src = ComposedTextSource.Build(
            MixedGroup(),
            ownerTable: "products",
            ownerSchema: "app",
            ownerKeyColumns: ["id"]);

        src.Columns.Should().HaveCount(3);

        var title = src.Columns.Single(c => c.PropertyName == "title");
        title.Kind.Should().Be(ComposedColumnKind.OwnerLocal);
        title.Weight.Should().Be(FullTextWeightBucket.A);
        title.Alias.Should().Be(ComposedTextSource.OwnerAlias);

        var category = src.Columns.Single(c => c.PropertyName == "category_name");
        category.Kind.Should().Be(ComposedColumnKind.Scalar);
        category.Weight.Should().Be(FullTextWeightBucket.B);
        category.Alias.Should().Be("cat1");

        var tag = src.Columns.Single(c => c.PropertyName == "tag_name");
        tag.Kind.Should().Be(ComposedColumnKind.Aggregated);
        tag.Weight.Should().Be(FullTextWeightBucket.C);
        tag.Alias.Should().Be("tag1");
    }

    [Fact]
    public void Build_mixed_group_emits_qualified_left_joins()
    {
        var src = ComposedTextSource.Build(
            MixedGroup(),
            ownerTable: "products",
            ownerSchema: "app",
            ownerKeyColumns: ["id"]);

        src.Joins.Should().HaveCount(2);

        var catJoin = src.Joins.Single(j => j.Alias == "cat1");
        catJoin.TableName.Should().Be("categories");
        catJoin.Schema.Should().Be("catalog");
        catJoin.Cardinality.Should().Be(JoinCardinality.ManyToOne);
        catJoin.OnClause.Should().Be("\"cat1\".\"id\" = \"e\".\"category_id\"");

        var tagJoin = src.Joins.Single(j => j.Alias == "tag1");
        tagJoin.TableName.Should().Be("tags");
        tagJoin.Schema.Should().Be("tagging");
        tagJoin.Cardinality.Should().Be(JoinCardinality.OneToMany);
        tagJoin.OnClause.Should().Be("\"tag1\".\"product_id\" = \"e\".\"id\"");
    }

    [Fact]
    public void Build_many_to_one_on_clause_uses_referenced_key_column()
    {
        var prop = new FullTextGroupProperty
        {
            PropertyName = "warehouse_name",
            ColumnName = "name",
            Weight = FullTextWeightBucket.B,
            Join = new JoinPath
            {
                Hops =
                [
                    new JoinHop
                    {
                        TableName = "warehouses",
                        TableAlias = "wh1",
                        ForeignKeyColumn = "warehouse_id",
                        EntityType = typeof(object),
                        Cardinality = JoinCardinality.ManyToOne,
                        ForeignKeyOwningSide = true,
                        ReferencedKeyColumn = "code",
                    },
                ],
            },
        };
        var group = new FullTextGroup
        {
            Name = "default",
            FullTextConfig = "english",
            Reindex = Abstractions.Attributes.ReindexMode.Inline,
            Properties = [prop],
        };

        var src = ComposedTextSource.Build(
            group, ownerTable: "orders", ownerSchema: null, ownerKeyColumns: ["id"]);

        var join = src.Joins.Single(j => j.Alias == "wh1");
        join.OnClause.Should().Be("\"wh1\".\"code\" = \"e\".\"warehouse_id\"");
    }

    [Fact]
    public void Build_uses_owner_keys_as_group_by()
    {
        var src = ComposedTextSource.Build(
            MixedGroup(),
            ownerTable: "products",
            ownerSchema: "app",
            ownerKeyColumns: ["tenant_id", "id"]);

        src.GroupByKeys.Should().Equal("tenant_id", "id");
    }

    [Fact]
    public void Build_all_owner_local_group_yields_no_joins()
    {
        var group = new FullTextGroup
        {
            Name = "default",
            FullTextConfig = "english",
            Reindex = Abstractions.Attributes.ReindexMode.Inline,
            Properties =
            [
                OwnerLocal("title", FullTextWeightBucket.A),
                OwnerLocal("body", FullTextWeightBucket.B),
            ],
        };

        var src = ComposedTextSource.Build(
            group,
            ownerTable: "products",
            ownerSchema: null,
            ownerKeyColumns: ["id"]);

        src.Joins.Should().BeEmpty();
        src.Columns.Should().OnlyContain(c => c.Kind == ComposedColumnKind.OwnerLocal);
        src.Columns.Should().OnlyContain(c => c.Alias == ComposedTextSource.OwnerAlias);
    }
}
