using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ferret.EntityFrameworkCore.Tests;

public class SessionSmokeTests
{
    public sealed class Widget
    {
        public Guid Id { get; init; }
        public string Name { get; set; } = "";
    }

    private sealed class TestContext : DbContext
    {
        public TestContext(DbContextOptions<TestContext> opts) : base(opts) { }
        public DbSet<Widget> Widgets => Set<Widget>();
    }

    [Fact]
    public async Task OpenConnectionAsync_returns_open_connection()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        await conn.OpenAsync();
        var opts = new DbContextOptionsBuilder<TestContext>().UseSqlite(conn).Options;
        await using var ctx = new TestContext(opts);
        await ctx.Database.EnsureCreatedAsync();

        await using var session = new EntityFrameworkSession(ctx, new PostgresDialect());
        var opened = await session.OpenConnectionAsync(default);

        opened.State.Should().Be(System.Data.ConnectionState.Open);
    }
}
