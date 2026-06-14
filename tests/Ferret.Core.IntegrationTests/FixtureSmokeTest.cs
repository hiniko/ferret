using Ferret.Core.IntegrationTests.Fixtures;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Ferret.Core.IntegrationTests;

[Collection("postgres")]
public class FixtureSmokeTest
{
    private readonly PostgresFixture _fx;

    public FixtureSmokeTest(PostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task pg_trgm_extension_is_loaded()
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT extname FROM pg_extension WHERE extname = 'pg_trgm'";
        var result = (string?)await cmd.ExecuteScalarAsync();
        result.Should().Be("pg_trgm");
    }
}
