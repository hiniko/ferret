using Ferret.Core.Backends.Hybrid;
using FluentAssertions;
using Xunit;

namespace Ferret.Core.Tests.Backends.Hybrid;

public class HybridSqlBuilderTests
{
    [Fact]
    public void Composes_ctes_union_and_rrf()
    {
        var b = new HybridSqlBuilder(new PostgresDialect());
        var frag = b.Build(new HybridSqlContext
        {
            KeyColumns = new[] { "id" },
            Limit = 20, Offset = 0, RrfK = 60,
            Backends = new[]
            {
                new HybridBackendFragment { CteName = "ft",  Body = new SearchSqlFragment("SELECT id, row_number() OVER (ORDER BY rank DESC) AS rnk FROM ftcore", System.Array.Empty<System.Collections.Generic.KeyValuePair<string, object?>>()), Weight = 1.0 },
                new HybridBackendFragment { CteName = "vec", Body = new SearchSqlFragment("SELECT id, row_number() OVER (ORDER BY d) AS rnk FROM veccore", System.Array.Empty<System.Collections.Generic.KeyValuePair<string, object?>>()), Weight = 2.0 },
            },
        });

        frag.Sql.Should().Contain("WITH ft AS");
        frag.Sql.Should().Contain("vec AS");
        frag.Sql.Should().Contain("UNION ALL");
        frag.Sql.Should().Contain("(60 + r.rnk)");
        frag.Sql.Should().Contain("COUNT(*) OVER() AS total_count");
        frag.Sql.Should().Contain("ORDER BY rrf DESC");
        frag.Sql.Should().Contain("LIMIT 20 OFFSET 0");
    }

    [Fact]
    public void Fused_order_has_key_tiebreaker_for_stable_paging()
    {
        var b = new HybridSqlBuilder(new PostgresDialect());
        var frag = b.Build(new HybridSqlContext
        {
            KeyColumns = new[] { "id" },
            Limit = 20, Offset = 0, RrfK = 60,
            Backends = new[]
            {
                new HybridBackendFragment { CteName = "ft",  Body = new SearchSqlFragment("SELECT id, row_number() OVER (ORDER BY rank DESC) AS rnk FROM ftcore", System.Array.Empty<System.Collections.Generic.KeyValuePair<string, object?>>()), Weight = 1.0 },
            },
        });

        frag.Sql.Should().Contain("ORDER BY rrf DESC, ");
        frag.Sql.Should().MatchRegex("ORDER BY rrf DESC,\\s*\"id\"");
    }

    [Fact]
    public void Merges_backend_parameters()
    {
        var b = new HybridSqlBuilder(new PostgresDialect());
        var frag = b.Build(new HybridSqlContext
        {
            KeyColumns = new[] { "id" }, Limit = 10, Offset = 0, RrfK = 60,
            Backends = new[]
            {
                new HybridBackendFragment { CteName = "ft", Body = new SearchSqlFragment("SELECT id, row_number() OVER (ORDER BY rank DESC) AS rnk FROM x", new[] { new System.Collections.Generic.KeyValuePair<string, object?>("@q", "hi") }), Weight = 1.0 },
            },
        });
        frag.Parameters.Should().ContainSingle(p => p.Key == "@q");
    }
}
