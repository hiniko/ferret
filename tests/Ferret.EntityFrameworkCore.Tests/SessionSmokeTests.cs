using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ferret.EntityFrameworkCore.Tests;

[Collection("postgres")]
public class SessionSmokeTests
{
    private readonly PostgresFixture _fixture;

    public SessionSmokeTests(PostgresFixture fixture) => _fixture = fixture;

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
        var opts = new DbContextOptionsBuilder<TestContext>()
            .UseNpgsql(_fixture.UniqueConnectionString())
            .Options;
        await using var ctx = new TestContext(opts);
        await ctx.Database.EnsureCreatedAsync();

        await using var session = new EntityFrameworkSession(ctx, new PostgresDialect());
        var opened = await session.OpenConnectionAsync(default);

        opened.State.Should().Be(System.Data.ConnectionState.Open);
    }
}
