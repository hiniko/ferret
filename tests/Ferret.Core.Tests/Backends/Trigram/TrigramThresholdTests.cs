using Ferret.Abstractions;
using FluentAssertions;
using Xunit;

namespace Ferret.Core.Tests.Backends.Trigram;

public class TrigramThresholdTests
{
    [SearchableEntity]
    public sealed class Product : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }
        [Searchable] public string Name { get; init; } = "";
    }

    [Theory]
    [InlineData(0.35, "0.65")]
    [InlineData(0.7, "0.30")]
    [InlineData(0.5, "0.50")]
    public void Threshold_is_tunable_via_options(double minSim, string expectedMaxDistance)
    {
        var opts = new TrigramOptions { MinimumSimilarity = minSim };
        var b = new TrigramSqlBuilder(new PostgresDialect(), opts);
        var model = EntityRegistry.Build([typeof(Product)], new SnakeCaseNamingStrategy()).Get<Product>();

        var sql = b.BuildRanking(new SearchContext
        {
            Properties = model.SearchableProperties,
            SearchTerm = "x",
            IdColumn = model.KeyColumnName,
            QuotedTable = model.QuotedTable(new PostgresDialect()),
        }, page: 0, pageSize: 25).Sql;

        sql.Should().Contain($"distance <= {expectedMaxDistance}");
    }
}
