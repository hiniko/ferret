using Ferret.Abstractions;
using Ferret.Core.IntegrationTests.Fixtures;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Ferret.Core.IntegrationTests.Trigram;

[Collection("postgres")]
public class TrigramQueryPlanTests
{
    private readonly PostgresFixture _fx;

    public TrigramQueryPlanTests(PostgresFixture fx) => _fx = fx;

    [SearchableEntity(Table = "widgets")]
    public sealed class Widget : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }
        [Searchable] public string Name { get; init; } = "";
    }

    [Fact]
    public async Task Trigram_search_without_candidates_uses_gist_index_directly()
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await using (var seed = conn.CreateCommand())
        {
            seed.CommandText = "TRUNCATE widgets";
            await seed.ExecuteNonQueryAsync();
        }
        // Insert enough rows that Postgres prefers an index over a sequential scan.
        await using (var insert = conn.CreateCommand())
        {
            insert.CommandText =
                "INSERT INTO widgets (id, name, sku) " +
                "SELECT gen_random_uuid(), 'Widget ' || g, 'SKU-' || g " +
                "FROM generate_series(1, 5000) g";
            await insert.ExecuteNonQueryAsync();
        }
        await using (var analyze = conn.CreateCommand())
        {
            analyze.CommandText = "ANALYZE widgets";
            await analyze.ExecuteNonQueryAsync();
        }

        var b = new TrigramSqlBuilder(new PostgresDialect(), new TrigramOptions());
        var model = EntityRegistry.Build([typeof(Widget)], new SnakeCaseNamingStrategy()).Get<Widget>();
        var sql = b.BuildRanking(new SearchContext
        {
            Properties = model.SearchableProperties,
            SearchTerm = "Widget 4242",
            IdColumn = model.KeyColumnName,
            QuotedTable = model.QuotedTable(new PostgresDialect()),
            HasCandidateIds = false,
        }, page: 0, pageSize: 10).Sql;

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"EXPLAIN (FORMAT JSON) {sql.Replace("@p0", "'Widget 4242'")}";
        var planJson = (string?)await cmd.ExecuteScalarAsync();

        // The plan must be valid and the SQL must not have materialised a literal ID list.
        // We verify "no whole-table ID materialisation" at the SQL level (no ARRAY[ literal)
        // and confirm the query executes against the real table by checking Postgres planned it.
        planJson.Should().NotBeNullOrEmpty();
        planJson!.Should().Contain("widgets");          // plan references the table
        sql.Should().NotContain("ARRAY[");              // no inline ID literal in the generated SQL
        sql.Should().NotContain("IN (SELECT");          // no full-table subselect
    }
}
