using Ferret.Abstractions;
using Ferret.Core.Backends.FullText;
using Ferret.Core.Backends.Hybrid;
using Ferret.Core.Backends.Trigram;
using Ferret.Core.Backends.Vector;
using FluentAssertions;
using Xunit;

namespace Ferret.Core.Tests.Backends.Hybrid;

public class RankedCandidateTests
{
    [Fact]
    public void Vector_ranked_candidate_has_rnk_threshold_and_nested_knn()
    {
        var b = new VectorSqlBuilder(new PostgresDialect(), new VectorOptions());
        var frag = b.BuildRankedCandidate(
            new RankedCandidateRequest
            {
                SourceTable = "vdocs", KeyColumns = new[] { "id" }, SearchTerm = "x",
                Depth = 100, CteName = "vec", QueryVectorParameterName = "@qvec",
                ConfidenceThreshold = 0.3,
            },
            sidecarTable: "vdocs_vec", groupColumn: "content_embedding");

        frag.Sql.Should().Contain("row_number() OVER (ORDER BY");
        frag.Sql.Should().Contain("<=> (@qvec)::vector");
        frag.Sql.Should().Contain("LIMIT 100");
        frag.Sql.Should().Contain("0.3");
        frag.Sql.Should().NotContain("total_count");
        frag.Sql.Should().NotContain("WITH ");
    }

    [Fact]
    public void Vector_ranked_candidate_omits_threshold_when_null()
    {
        var b = new VectorSqlBuilder(new PostgresDialect(), new VectorOptions());
        var frag = b.BuildRankedCandidate(
            new RankedCandidateRequest
            {
                SourceTable = "vdocs", KeyColumns = new[] { "id" }, SearchTerm = "x",
                Depth = 50, CteName = "vec", QueryVectorParameterName = "@qvec",
            },
            sidecarTable: "vdocs_vec", groupColumn: "content_embedding");

        frag.Sql.Should().Contain("LIMIT 50");
        frag.Sql.Should().NotContain("::vector <=");   // no distance threshold gate
    }

    [Fact]
    public void FullText_ranked_candidate_has_rnk_threshold_and_limit()
    {
        var groups = new List<FullTextGroup>
        {
            new()
            {
                Name = "content", FullTextConfig = "english", Reindex = ReindexMode.Inline,
                Properties = [ new() { PropertyName = "Name", ColumnName = "name", Weight = FullTextWeightBucket.A } ],
            }
        };
        var ctx = new FullTextSqlContext
        {
            SourceTable = "products", IdColumn = "id", SearchTerm = "blue widget",
            Groups = groups, Limit = 0, Offset = 0,
        };

        var b = new FullTextSqlBuilder(new PostgresDialect(), new FullTextOptions());
        var frag = b.BuildRankedCandidate(
            new RankedCandidateRequest
            {
                SourceTable = "products", KeyColumns = new[] { "id" }, SearchTerm = "blue widget",
                Depth = 100, CteName = "ft", ConfidenceThreshold = 0.05,
            },
            ctx);

        frag.Sql.Should().Contain("row_number() OVER (ORDER BY");
        frag.Sql.Should().Contain("ts_rank_cd");
        frag.Sql.Should().Contain("LIMIT 100");
        frag.Sql.Should().Contain("0.05");
        frag.Sql.Should().NotContain("total_count");
        frag.Parameters.Should().ContainSingle(p => p.Key == "@q");
    }

    [Fact]
    public void FullText_ranked_candidate_omits_threshold_when_null()
    {
        var groups = new List<FullTextGroup>
        {
            new()
            {
                Name = "content", FullTextConfig = "english", Reindex = ReindexMode.Inline,
                Properties = [ new() { PropertyName = "Name", ColumnName = "name", Weight = FullTextWeightBucket.A } ],
            }
        };
        var ctx = new FullTextSqlContext
        {
            SourceTable = "products", IdColumn = "id", SearchTerm = "x",
            Groups = groups, Limit = 0, Offset = 0,
        };

        var b = new FullTextSqlBuilder(new PostgresDialect(), new FullTextOptions());
        var frag = b.BuildRankedCandidate(
            new RankedCandidateRequest
            {
                SourceTable = "products", KeyColumns = new[] { "id" }, SearchTerm = "x",
                Depth = 25, CteName = "ft",
            },
            ctx);

        frag.Sql.Should().Contain("LIMIT 25");
        frag.Sql.Should().NotContain(">=");
    }

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
    public void Trigram_ranked_candidate_has_rnk_threshold_and_limit()
    {
        var b = new TrigramSqlBuilder(new PostgresDialect(), new TrigramOptions());
        var ctx = new SearchContext
        {
            Properties = Model().SearchableProperties,
            SearchTerm = "blue",
            IdColumn = Model().KeyColumnName,
            QuotedTable = Model().QuotedTable(new PostgresDialect()),
        };

        var frag = b.BuildRankedCandidate(
            new RankedCandidateRequest
            {
                SourceTable = "products", KeyColumns = new[] { "id" }, SearchTerm = "blue",
                Depth = 100, CteName = "tri", ConfidenceThreshold = 0.2,
            },
            ctx);

        frag.Sql.Should().Contain("row_number() OVER (ORDER BY");
        frag.Sql.Should().Contain("<<->");
        frag.Sql.Should().Contain("LIMIT 100");
        frag.Sql.Should().Contain("distance");
        frag.Sql.Should().Contain("0.2");
        frag.Sql.Should().NotContain("total_count");
        frag.Parameters.Should().Contain(p => p.Key == "@p0");
    }

    [Fact]
    public void Trigram_ranked_candidate_uses_max_distance_when_threshold_null()
    {
        var b = new TrigramSqlBuilder(new PostgresDialect(), new TrigramOptions());
        var ctx = new SearchContext
        {
            Properties = Model().SearchableProperties,
            SearchTerm = "blue",
            IdColumn = Model().KeyColumnName,
            QuotedTable = Model().QuotedTable(new PostgresDialect()),
        };

        var frag = b.BuildRankedCandidate(
            new RankedCandidateRequest
            {
                SourceTable = "products", KeyColumns = new[] { "id" }, SearchTerm = "blue",
                Depth = 40, CteName = "tri",
            },
            ctx);

        frag.Sql.Should().Contain("LIMIT 40");
        frag.Sql.Should().Contain("0.65");   // 1 - 0.35 default MaxDistance
    }
}
