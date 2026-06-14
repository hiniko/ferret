using System.Data.Common;
using Ferret.Abstractions.Querying;
using Ferret.Core.Engine;
using Ferret.Core.Sql;
using Ferret.EntityFrameworkCore;
using Ferret.EntityFrameworkCore.DependencyInjection;
using Ferret.EntityFrameworkCore.Querying;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ferret.EntityFrameworkCore.Tests.Querying;

public sealed class EntityFrameworkQueryServiceTests
{
    public sealed class Product
    {
        public int Id { get; init; }
    }

    private sealed class TestContext : DbContext
    {
        public TestContext(DbContextOptions<TestContext> opts) : base(opts) { }
        public DbSet<Product> Products => Set<Product>();
    }

    private sealed class FakeEngine : IFerretEngine
    {
        public IFerretSession? OffsetSession;
        public object? OffsetQuery;
        public CancellationToken OffsetToken;
        public object? OffsetResultToReturn;

        public IFerretSession? CursorSession;
        public object? CursorQuery;
        public CancellationToken CursorToken;
        public object? CursorResultToReturn;

        public Task<OffsetResult<T>> SearchOffsetAsync<T, TKey>(
            IFerretSession session,
            PagedQuery<T, TKey> query,
            CancellationToken ct = default)
            where T : class
            where TKey : notnull
        {
            OffsetSession = session;
            OffsetQuery = query;
            OffsetToken = ct;
            return Task.FromResult((OffsetResult<T>)OffsetResultToReturn!);
        }

        public Task<CursorResult<T>> SearchCursorAsync<T, TKey>(
            IFerretSession session,
            PagedQuery<T, TKey> query,
            CancellationToken ct = default)
            where T : class
            where TKey : notnull
        {
            CursorSession = session;
            CursorQuery = query;
            CursorToken = ct;
            return Task.FromResult((CursorResult<T>)CursorResultToReturn!);
        }

        public Task ReindexAsync<T>(
            IFerretSession session,
            string group,
            ReindexOptions? options = null,
            CancellationToken ct = default)
            where T : class
            => Task.CompletedTask;
    }

    private static ServiceProvider BuildProvider(SqliteConnection conn, FakeEngine engine)
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestContext>(o => o.UseSqlite(conn));
        services.AddSingleton<ISqlDialect>(new PostgresDialect());
        services.AddSingleton<IFerretEngine>(engine);
        services.AddFerretEntityFrameworkQueryService<TestContext>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task SearchOffsetAsync_opens_session_from_context_and_forwards_to_engine()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        await conn.OpenAsync();

        var expected = new OffsetResult<Product> { Items = [] };
        var engine = new FakeEngine { OffsetResultToReturn = expected };
        await using var sp = BuildProvider(conn, engine);

        using var scope = sp.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IFerretQueryService>();

        var query = new PagedQuery<Product, int> { Limit = 7 };
        using var cts = new CancellationTokenSource();

        var result = await svc.SearchOffsetAsync(query, cts.Token);

        result.Should().BeSameAs(expected);
        engine.OffsetSession.Should().BeOfType<EntityFrameworkSession>();
        engine.OffsetQuery.Should().BeSameAs(query);
        engine.OffsetToken.Should().Be(cts.Token);
    }

    [Fact]
    public async Task SearchCursorAsync_opens_session_from_context_and_forwards_to_engine()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        await conn.OpenAsync();

        var expected = new CursorResult<Product> { Items = [] };
        var engine = new FakeEngine { CursorResultToReturn = expected };
        await using var sp = BuildProvider(conn, engine);

        using var scope = sp.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IFerretQueryService>();

        var query = new PagedQuery<Product, int> { Mode = PaginationMode.Cursor, Limit = 9 };
        using var cts = new CancellationTokenSource();

        var result = await svc.SearchCursorAsync(query, cts.Token);

        result.Should().BeSameAs(expected);
        engine.CursorSession.Should().BeOfType<EntityFrameworkSession>();
        engine.CursorQuery.Should().BeSameAs(query);
        engine.CursorToken.Should().Be(cts.Token);
    }

    [Fact]
    public void Registers_scoped_IFerretQueryService()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        var services = new ServiceCollection();
        services.AddDbContext<TestContext>(o => o.UseSqlite(conn));
        services.AddSingleton<ISqlDialect>(new PostgresDialect());
        services.AddSingleton<IFerretEngine>(new FakeEngine());
        services.AddFerretEntityFrameworkQueryService<TestContext>();

        var descriptor = services.Single(d => d.ServiceType == typeof(IFerretQueryService));
        descriptor.Lifetime.Should().Be(ServiceLifetime.Scoped);
        descriptor.ImplementationType.Should().Be<EntityFrameworkQueryService<TestContext>>();
    }
}
