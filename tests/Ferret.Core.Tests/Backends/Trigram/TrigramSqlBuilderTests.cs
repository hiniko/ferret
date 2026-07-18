using Ferret.Abstractions;
using FluentAssertions;
using VerifyXunit;
using Xunit;

namespace Ferret.Core.Tests.Backends.Trigram;

public class TrigramSqlBuilderTests
{
    [SearchableEntity]
    private sealed class Product : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }
        [Searchable] public string Name { get; init; } = "";
        [Searchable(Weight = 2.0f)] public string Sku { get; init; } = "";
    }

    private static EntityModel Model() => EntityRegistry
        .Build([typeof(Product)], new SnakeCaseNamingStrategy())
        .Get<Product>();

    [Fact]
    public Task Snapshot_direct_field_search_uses_candidate_parameter_not_inline_literal()
    {
        var b = new TrigramSqlBuilder(new PostgresDialect(), new TrigramOptions());
        var ctx = new SearchContext
        {
            Properties = Model().SearchableProperties,
            SearchTerm = "blue",
            IdColumn = Model().KeyColumnName,
            QuotedTable = Model().QuotedTable(new PostgresDialect()),
            HasCandidateIds = true,
        };
        var fragment = b.BuildRanking(ctx, page: 0, pageSize: 25);
        return Verifier.Verify(fragment.Sql, extension: "sql");
    }

    [Fact]
    public void Direct_search_without_candidate_ids_omits_candidate_join()
    {
        var b = new TrigramSqlBuilder(new PostgresDialect(), new TrigramOptions());
        var ctx = new SearchContext
        {
            Properties = Model().SearchableProperties,
            SearchTerm = "blue",
            IdColumn = Model().KeyColumnName,
            QuotedTable = Model().QuotedTable(new PostgresDialect()),
            HasCandidateIds = false,
        };
        var fragment = b.BuildRanking(ctx, page: 0, pageSize: 25);
        fragment.Sql.Should().NotContain("candidate_ids");
        fragment.Sql.Should().NotContain("ARRAY[");
    }

    [Fact]
    public void MinimumSimilarity_option_drives_distance_threshold()
    {
        var opts = new TrigramOptions { MinimumSimilarity = 0.7 };
        var b = new TrigramSqlBuilder(new PostgresDialect(), opts);
        var ctx = new SearchContext
        {
            Properties = Model().SearchableProperties,
            SearchTerm = "blue",
            IdColumn = Model().KeyColumnName,
            QuotedTable = Model().QuotedTable(new PostgresDialect()),
        };
        var fragment = b.BuildRanking(ctx, page: 0, pageSize: 25);
        fragment.Sql.Should().Contain("0.30");                // 1 - 0.7 = 0.30
    }

    [SearchableEntity]
    private sealed class Order : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }
        [Searchable] public string Reference { get; init; } = "";
        [SearchJoin(ForeignKey = "order_id", Where = "{c}.deleted_at IS NULL AND {c}.hidden = false")]
        public List<OrderItem> Items { get; init; } = [];
        [SearchJoin(ForeignKey = "supplier_id")]
        public Supplier? Supplier { get; init; }
    }

    private sealed class OrderItem
    {
        public Guid Id { get; init; }
        [Searchable] public string Description { get; init; } = "";
    }

    [SearchableEntity(Table = "suppliers")]
    private sealed class Supplier
    {
        public Guid Id { get; init; }
        [Searchable] public string CompanyName { get; init; } = "";
    }

    private static EntityModel OrderModel() => EntityRegistry
        .Build([typeof(Order)], new SnakeCaseNamingStrategy())
        .Get<Order>();

    [Fact]
    public void Join_where_condition_is_alias_substituted_into_join_arm()
    {
        var b = new TrigramSqlBuilder(new PostgresDialect(), new TrigramOptions());
        var ctx = new SearchContext
        {
            Properties = OrderModel().SearchableProperties,
            SearchTerm = "blue",
            IdColumn = OrderModel().KeyColumnName,
            QuotedTable = OrderModel().QuotedTable(new PostgresDialect()),
        };
        var fragment = b.BuildRanking(ctx, page: 0, pageSize: 25);
        fragment.Sql.Should().MatchRegex(
            @"WHERE \((\w+)\.deleted_at IS NULL AND \1\.hidden = false\)");
        fragment.Sql.Should().NotContain("{c}");
    }

    [Fact]
    public void ManyToOne_hop_joins_previous_fk_to_referenced_key()
    {
        var b = new TrigramSqlBuilder(new PostgresDialect(), new TrigramOptions());
        var ctx = new SearchContext
        {
            Properties = OrderModel().SearchableProperties,
            SearchTerm = "blue",
            IdColumn = OrderModel().KeyColumnName,
            QuotedTable = OrderModel().QuotedTable(new PostgresDialect()),
        };
        var fragment = b.BuildRanking(ctx, page: 0, pageSize: 25);
        // N:1 (Supplier): owner carries the FK — ON e."supplier_id" = <alias>."id"
        fragment.Sql.Should().MatchRegex(@"INNER JOIN ""suppliers"" \w+ ON e\.""supplier_id"" = \w+\.""id""");
        // 1:N (Items): child carries the FK — ON <alias>."order_id" = e."id"
        fragment.Sql.Should().MatchRegex(@"INNER JOIN ""order_items"" \w+ ON \w+\.""order_id"" = e\.""id""");
    }
}
