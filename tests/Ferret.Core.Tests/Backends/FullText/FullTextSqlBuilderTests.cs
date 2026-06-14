using Ferret.Core.Backends.FullText;
using FluentAssertions;
using Xunit;

namespace Ferret.Core.Tests.Backends.FullText;

public sealed class FullTextSqlBuilderTests
{
    private static readonly ISqlDialect Dialect = new PostgresDialect();
    private static readonly FullTextOptions Opts = new();

    [Fact]
    public void Single_group_emits_expected_sql()
    {
        var groups = new List<FullTextGroup>
        {
            new()
            {
                Name = "content",
                FullTextConfig = "english",
                Reindex = ReindexMode.Inline,
                Properties =
                [
                    new() { PropertyName = "Name",        ColumnName = "name",        Weight = FullTextWeightBucket.A },
                    new() { PropertyName = "Description", ColumnName = "description", Weight = FullTextWeightBucket.B },
                ],
            }
        };

        var ctx = new FullTextSqlContext
        {
            SourceTable = "products",
            IdColumn = "id",
            SearchTerm = "blue widget",
            Groups = groups,
            Limit = 25,
            Offset = 0,
        };

        var fragment = new FullTextSqlBuilder(Dialect, Opts).BuildRanking(ctx);

        fragment.Sql.Should().Contain("FROM \"products_search\"")
                  .And.Contain("websearch_to_tsquery('english',")
                  .And.Contain("ts_rank_cd(s.\"content_tsv\"")
                  .And.Contain("ORDER BY rank DESC");
        fragment.Parameters.Should().ContainSingle(p => p.Key == "@q" && (string)p.Value! == "blue widget");
    }

    [Fact]
    public void Multi_group_distinct_configs_emit_one_cross_join_per_config()
    {
        var groups = new List<FullTextGroup>
        {
            new() { Name = "content", FullTextConfig = "english", Reindex = ReindexMode.Inline,
                    Properties = [ new() { PropertyName = "Name", ColumnName = "name", Weight = FullTextWeightBucket.A } ] },
            new() { Name = "tags",    FullTextConfig = "simple",  Reindex = ReindexMode.Inline,
                    Properties = [ new() { PropertyName = "Tags", ColumnName = "tags", Weight = FullTextWeightBucket.A } ] },
        };
        var ctx = new FullTextSqlContext
        {
            SourceTable = "products", IdColumn = "id", SearchTerm = "x",
            Groups = groups, Limit = 10, Offset = 0,
        };

        var sql = new FullTextSqlBuilder(Dialect, Opts).BuildRanking(ctx).Sql;

        sql.Should().Contain("websearch_to_tsquery('english',")
           .And.Contain("websearch_to_tsquery('simple',")
           .And.Contain("GREATEST(");
    }

    [Fact]
    public void Sum_combinator_emits_addition_instead_of_GREATEST()
    {
        var opts = new FullTextOptions { GroupCombinator = GroupCombinator.Sum };
        var groups = new List<FullTextGroup>
        {
            new() { Name = "g1", FullTextConfig = "simple", Reindex = ReindexMode.Inline,
                    Properties = [ new() { PropertyName = "A", ColumnName = "a", Weight = FullTextWeightBucket.A } ] },
            new() { Name = "g2", FullTextConfig = "simple", Reindex = ReindexMode.Inline,
                    Properties = [ new() { PropertyName = "B", ColumnName = "b", Weight = FullTextWeightBucket.A } ] },
        };
        var ctx = new FullTextSqlContext
        {
            SourceTable = "products", IdColumn = "id", SearchTerm = "x",
            Groups = groups, Limit = 10, Offset = 0,
        };

        var sql = new FullTextSqlBuilder(Dialect, opts).BuildRanking(ctx).Sql;

        sql.Should().NotContain("GREATEST(").And.Contain(" + ");
    }

    [Fact]
    public void CandidateIds_filter_inner_joins_against_candidates()
    {
        var groups = new List<FullTextGroup>
        {
            new() { Name = "content", FullTextConfig = "english", Reindex = ReindexMode.Inline,
                    Properties = [ new() { PropertyName = "Name", ColumnName = "name", Weight = FullTextWeightBucket.A } ] },
        };
        var ctx = new FullTextSqlContext
        {
            SourceTable = "products", IdColumn = "id", SearchTerm = "x",
            Groups = groups, Limit = 10, Offset = 0,
            CandidateIdsParameterName = "@candidate_ids",
        };

        var sql = new FullTextSqlBuilder(Dialect, Opts).BuildRanking(ctx).Sql;

        sql.Should().Contain("unnest(@candidate_ids)")
           .And.Contain("INNER JOIN candidates");
    }

    [Fact]
    public void CompositeKey_candidate_filter_emits_ordinality_unnest_and_multicol_join()
    {
        var groups = new List<FullTextGroup>
        {
            new() { Name = "content", FullTextConfig = "english", Reindex = ReindexMode.Inline,
                    Properties = [ new() { PropertyName = "Name", ColumnName = "name", Weight = FullTextWeightBucket.A } ] },
        };
        var ctx = new FullTextSqlContext
        {
            SourceTable = "products", IdColumn = "tenant_id", SearchTerm = "x",
            Groups = groups, Limit = 10, Offset = 0,
            KeyColumns = ["tenant_id", "id"],
            CandidateKeyParameterNames = ["@candidate_k0", "@candidate_k1"],
            CandidateIdsParameterName = "@candidate_k0",
        };

        var sql = new FullTextSqlBuilder(Dialect, Opts).BuildRanking(ctx).Sql;

        sql.Should().Contain("WITH ORDINALITY")
           .And.Contain("unnest(@candidate_k0)")
           .And.Contain("unnest(@candidate_k1)")
           .And.Contain("USING (ord)")
           .And.Contain("c.\"tenant_id\" = s.\"tenant_id\"")
           .And.Contain("c.\"id\" = s.\"id\"")
           .And.Contain(" AND ");
    }

    [Theory]
    [InlineData(FullTextParser.Websearch, "websearch_to_tsquery")]
    [InlineData(FullTextParser.Plain,     "plainto_tsquery")]
    [InlineData(FullTextParser.Phrase,    "phraseto_tsquery")]
    [InlineData(FullTextParser.Raw,       "to_tsquery")]
    public void Parser_option_selects_tsquery_function(FullTextParser parser, string expectedFn)
    {
        var opts = new FullTextOptions { DefaultParser = parser };
        var groups = new List<FullTextGroup>
        {
            new() { Name = "content", FullTextConfig = "english", Reindex = ReindexMode.Inline,
                    Properties = [ new() { PropertyName = "N", ColumnName = "n", Weight = FullTextWeightBucket.A } ] },
        };
        var ctx = new FullTextSqlContext
        {
            SourceTable = "products", IdColumn = "id", SearchTerm = "x",
            Groups = groups, Limit = 10, Offset = 0,
        };

        new FullTextSqlBuilder(Dialect, opts).BuildRanking(ctx).Sql
            .Should().Contain(expectedFn + "(");
    }
}
