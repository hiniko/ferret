using Ferret.Core.Backends.Vector;
using FluentAssertions;
using Xunit;

namespace Ferret.Core.Tests.Backends.Vector;

public class VectorSqlBuilderTests
{
    [Fact]
    public void Builds_cosine_ordered_query_with_total_count()
    {
        var builder = new VectorSqlBuilder(new PostgresDialect(), new VectorOptions());
        var frag = builder.BuildRanking(new VectorSqlContext
        {
            SidecarTable = "products_vec",
            SidecarSchema = null,
            GroupColumn = "content_embedding",
            IdColumn = "id",
            KeyColumns = null,
            Limit = 10,
            Offset = 0,
            QueryVectorParameterName = "@qvec",
            EfSearch = 40,
            CandidateIdsParameterName = null,
            CandidateKeyParameterNames = null,
        });

        // total_count is a true count over the full (unbounded) candidate set, not a window over
        // the already-LIMIT-bounded KNN result — see docs/vector-filtering.md and
        // VectorOffsetPagingTests. The scalar subquery form keeps the ranked-rows subquery's
        // ORDER BY ... LIMIT untouched so the HNSW index scan is still eligible for it.
        frag.Sql.Should().Contain("SELECT COUNT(*) FROM \"products_vec\" s WHERE");
        frag.Sql.Should().Contain("AS total_count");
        frag.Sql.Should().Contain("<=> (@qvec)::vector");
        frag.Sql.Should().Contain("ORDER BY");
        frag.Sql.Should().Contain("LIMIT 10 OFFSET 0");
    }
}
