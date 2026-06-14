using Ferret.Abstractions.Naming;
using Ferret.Abstractions.Search;
using Ferret.Benchmarks.Model;
using Ferret.Core.Backends.Trigram;
using Ferret.Core.Engine;
using Ferret.Core.Sql;

namespace Ferret.Benchmarks.Infrastructure;

public static class GeneratedSqlCapture
{
    public static SearchSqlFragment Capture(int depth, string searchTerm)
    {
        var dialect = new PostgresDialect();
        var entityType = HopGraph.EntityTypeForDepth(depth);
        var model = EntityRegistry
            .Build([entityType], new SnakeCaseNamingStrategy())
            .Get(entityType);

        var deepest = model.SearchableProperties
            .Where(p => p.JoinPath.Depth == depth)
            .ToList();

        var ctx = new SearchContext
        {
            Properties = deepest,
            SearchTerm = searchTerm,
            IdColumn = model.KeyColumnName,
            QuotedTable = model.QuotedTable(dialect),
        };

        var backend = new TrigramSearchBackend(dialect, new TrigramOptions());
        return backend.BuildRankingQuery(ctx);
    }
}
