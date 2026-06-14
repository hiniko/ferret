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
}
