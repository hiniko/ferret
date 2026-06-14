using Ferret.Abstractions;
using FluentAssertions;
using Xunit;

namespace Ferret.Core.Tests.Backends.Trigram;

public class CandidateIdsParameterTests
{
    [SearchableEntity]
    public sealed class Product : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }
        [Searchable] public string Name { get; init; } = "";
    }

    [Fact]
    public void Candidate_ids_pass_via_parameter_not_inline_literal()
    {
        var b = new TrigramSqlBuilder(new PostgresDialect(), new TrigramOptions());
        var model = EntityRegistry.Build([typeof(Product)], new SnakeCaseNamingStrategy()).Get<Product>();

        var sql = b.BuildRanking(new SearchContext
        {
            Properties = model.SearchableProperties,
            SearchTerm = "x",
            IdColumn = model.KeyColumnName,
            QuotedTable = model.QuotedTable(new PostgresDialect()),
            HasCandidateIds = true,
        }, page: 0, pageSize: 25).Sql;

        sql.Should().Contain("@candidate_ids");
        sql.Should().NotContain("ARRAY[");
        sql.Should().NotContainEquivalentOf("CAST(ARRAY");
    }

    [Fact]
    public void CompositeKey_emits_ordinality_unnest_and_multicol_join()
    {
        var b = new TrigramSqlBuilder(new PostgresDialect(), new TrigramOptions());
        var model = EntityRegistry.Build([typeof(Product)], new SnakeCaseNamingStrategy()).Get<Product>();

        var sql = b.BuildRanking(new SearchContext
        {
            Properties = model.SearchableProperties,
            SearchTerm = "x",
            IdColumn = "tenant_id",
            KeyColumns = ["tenant_id", "id"],
            CandidateKeyParameterNames = ["@candidate_k0", "@candidate_k1"],
            QuotedTable = model.QuotedTable(new PostgresDialect()),
            HasCandidateIds = true,
        }, page: 0, pageSize: 25).Sql;

        sql.Should().Contain("WITH ORDINALITY");
        sql.Should().Contain("unnest(@candidate_k0)");
        sql.Should().Contain("unnest(@candidate_k1)");
        sql.Should().Contain("USING (ord)");
        sql.Should().Contain("cnd.\"tenant_id\" = e.\"tenant_id\"");
        sql.Should().Contain("cnd.\"id\" = e.\"id\"");
        sql.Should().Contain(" AND ");
    }
}
