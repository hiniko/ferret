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

        frag.Sql.Should().Contain("COUNT(*) OVER() AS total_count");
        frag.Sql.Should().Contain("<=> (@qvec)::vector");
        frag.Sql.Should().Contain("ORDER BY");
    }
}
