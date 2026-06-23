using Ferret.EntityFrameworkCore;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ferret.EntityFrameworkCore.Tests;

[Collection("postgres")]
public class CrudRepositoryTests
{
    private readonly PostgresFixture _fixture;

    public CrudRepositoryTests(PostgresFixture fixture) => _fixture = fixture;

    public sealed class PlainEntity
    {
        public Guid Id { get; init; }
        public string Name { get; set; } = "";
    }

    public sealed class TimestampedEntity : IHasTimestamps
    {
        public Guid Id { get; init; }
        public string Name { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    private sealed class TestContext : DbContext
    {
        public TestContext(DbContextOptions<TestContext> opts) : base(opts) { }
        public DbSet<PlainEntity> Plain => Set<PlainEntity>();
        public DbSet<TimestampedEntity> Stamped => Set<TimestampedEntity>();
    }

    private async Task<TestContext> NewContextAsync()
    {
        var opts = new DbContextOptionsBuilder<TestContext>()
            .UseNpgsql(_fixture.UniqueConnectionString())
            .Options;
        var ctx = new TestContext(opts);
        await ctx.Database.EnsureCreatedAsync();
        return ctx;
    }

    [Fact]
    public async Task Create_entity_without_IHasTimestamps_does_not_throw()
    {
        await using var ctx = await NewContextAsync();
        var repo = new CrudRepository<PlainEntity, Guid>(ctx);
        var entity = new PlainEntity { Id = Guid.NewGuid(), Name = "x" };

        var saved = await repo.CreateAsync(entity);

        saved.Should().BeSameAs(entity);
    }

    [Fact]
    public async Task Create_entity_with_IHasTimestamps_populates_both_timestamps()
    {
        await using var ctx = await NewContextAsync();
        var repo = new CrudRepository<TimestampedEntity, Guid>(ctx);
        var entity = new TimestampedEntity { Id = Guid.NewGuid(), Name = "x" };

        await repo.CreateAsync(entity);

        entity.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
        entity.UpdatedAt.Should().Be(entity.CreatedAt);
    }

    [Fact]
    public async Task Update_returns_same_entity_reference_no_extra_roundtrip()
    {
        await using var ctx = await NewContextAsync();
        var repo = new CrudRepository<PlainEntity, Guid>(ctx);
        var entity = new PlainEntity { Id = Guid.NewGuid(), Name = "before" };
        await repo.CreateAsync(entity);

        entity.Name = "after";
        var updated = await repo.UpdateAsync(entity);

        ReferenceEquals(entity, updated).Should().BeTrue(
            "old code refetched via GetWithRelations and returned a new instance; new code returns the same one");
    }

    [Fact]
    public async Task Update_only_bumps_UpdatedAt_when_entity_is_IHasTimestamps()
    {
        await using var ctx = await NewContextAsync();
        var repo = new CrudRepository<TimestampedEntity, Guid>(ctx);
        var entity = new TimestampedEntity { Id = Guid.NewGuid(), Name = "x" };
        await repo.CreateAsync(entity);

        var createdAt = entity.CreatedAt;
        await Task.Delay(10);
        entity.Name = "y";
        await repo.UpdateAsync(entity);

        entity.CreatedAt.Should().Be(createdAt);
        entity.UpdatedAt.Should().BeAfter(createdAt);
    }
}
