using Ferret.Abstractions;
using Ferret.Core.Sql;
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
        [Searchable(Weight = 2.0f)] [Filterable] public string Sku { get; init; } = "";
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
        var fragment = b.BuildRanking(ctx, limit: 25, offset: 0);
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
        var fragment = b.BuildRanking(ctx, limit: 25, offset: 0);
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
        var fragment = b.BuildRanking(ctx, limit: 25, offset: 0);
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
        var fragment = b.BuildRanking(ctx, limit: 25, offset: 0);
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
        var fragment = b.BuildRanking(ctx, limit: 25, offset: 0);
        // N:1 (Supplier): owner carries the FK — ON e."supplier_id" = <alias>."id"
        fragment.Sql.Should().MatchRegex(@"INNER JOIN ""suppliers"" \w+ ON e\.""supplier_id"" = \w+\.""id""");
        // 1:N (Items): child carries the FK — ON <alias>."order_id" = e."id"
        fragment.Sql.Should().MatchRegex(@"INNER JOIN ""order_items"" \w+ ON \w+\.""order_id"" = e\.""id""");
    }

    [Fact]
    public void Ranking_applies_caller_limit_and_offset()
    {
        var b = new TrigramSqlBuilder(new PostgresDialect(), new TrigramOptions());
        var ctx = new SearchContext
        {
            Properties = Model().SearchableProperties,
            SearchTerm = "blue",
            IdColumn = Model().KeyColumnName,
            QuotedTable = Model().QuotedTable(new PostgresDialect()),
        };

        var fragment = b.BuildRanking(ctx, limit: 2, offset: 10);

        fragment.Sql.Should().Contain("LIMIT 2").And.Contain("OFFSET 10");
        fragment.Sql.Should().NotContain(int.MaxValue.ToString(),
            "an unbounded LIMIT means the caller's page size is silently ignored");
    }

    [Fact]
    public void CandidatePredicate_pushes_filter_into_candidates_cte_without_materialising_keys()
    {
        var model = Model();
        var meta = EntityMetadata.From(model, new PostgresDialect());
        var predicate = PagedSqlBuilder.CompileFilter(
            new() { Field = "Sku", Operator = FilterOperator.Equals, Value = "BLUE-001" },
            meta, parameterIndex: 0, tableAlias: "e", parameterPrefix: "f");

        var b = new TrigramSqlBuilder(new PostgresDialect(), new TrigramOptions());
        var ctx = new SearchContext
        {
            Properties = model.SearchableProperties,
            SearchTerm = "blue",
            IdColumn = model.KeyColumnName,
            QuotedTable = model.QuotedTable(new PostgresDialect()),
            HasCandidateIds = false,
            CandidatePredicateSql = predicate.Sql,
        };

        var sql = b.BuildRanking(ctx, limit: 25, offset: 0).Sql;

        // The predicate lives inside the candidates CTE (base-table select), not a WHERE tacked
        // onto the final result — and there is no unnest/array-materialisation of a candidate_ids
        // parameter anywhere, proving this is a single round trip rather than filter-then-fetch.
        sql.Should().Contain("WITH candidates AS (SELECT")
           .And.Contain("FROM \"products\" e WHERE e.\"sku\" = @f0")
           .And.Contain("INNER JOIN candidates cnd")
           .And.NotContain("candidate_ids")
           .And.NotContain("unnest(");
    }

    [Fact]
    public void CandidatePredicate_combines_with_CandidateKeys_by_intersection()
    {
        var model = Model();
        var meta = EntityMetadata.From(model, new PostgresDialect());
        var predicate = PagedSqlBuilder.CompileFilter(
            new() { Field = "Sku", Operator = FilterOperator.Equals, Value = "BLUE-001" },
            meta, parameterIndex: 0, tableAlias: "e", parameterPrefix: "f");

        var b = new TrigramSqlBuilder(new PostgresDialect(), new TrigramOptions());
        var ctx = new SearchContext
        {
            Properties = model.SearchableProperties,
            SearchTerm = "blue",
            IdColumn = model.KeyColumnName,
            QuotedTable = model.QuotedTable(new PostgresDialect()),
            HasCandidateIds = true,
            CandidatePredicateSql = predicate.Sql,
        };

        var sql = b.BuildRanking(ctx, limit: 25, offset: 0).Sql;

        // Both restrictions apply to the same base-table select: filter predicate AND array membership.
        sql.Should().Contain("FROM \"products\" e WHERE e.\"sku\" = @f0 AND e.\"id\" = ANY(@candidate_ids)");
    }

    [Fact]
    public void Backend_threads_context_limit_into_ranking_sql()
    {
        var backend = new TrigramSearchBackend(new PostgresDialect(), new TrigramOptions());
        var ctx = new SearchContext
        {
            Properties = Model().SearchableProperties,
            SearchTerm = "blue",
            IdColumn = Model().KeyColumnName,
            QuotedTable = Model().QuotedTable(new PostgresDialect()),
            Limit = 5,
            Offset = 15,
        };

        // Regression: the backend used to hardcode pageSize: int.MaxValue, dropping the
        // caller's limit entirely. The engine does not truncate ranked ids on the offset
        // path, so every trigram search returned the whole match set.
        backend.BuildRankingQuery(ctx).Sql.Should().Contain("LIMIT 5").And.Contain("OFFSET 15");
    }
}
