using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Ferret.Abstractions.Hydration;
using Ferret.Abstractions.Models;
using Ferret.Abstractions.Querying;
using Ferret.Abstractions.Session;
using Ferret.Abstractions.Sql;
using Ferret.Core.Engine;
using Ferret.Core.Querying;
using FluentAssertions;
using Xunit;

namespace Ferret.Core.Tests.Querying;

public sealed class FerretCoreQueryServiceTests
{
    private sealed class Product
    {
        public int Id { get; init; }
    }

    private sealed class FakeSession : IFerretSession
    {
        public ISqlDialect Dialect => null!;
        public IEntityHydrator Hydrator => null!;
        public Task<DbConnection> OpenConnectionAsync(CancellationToken ct) => throw new System.NotImplementedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeEngine : IFerretEngine
    {
        public IFerretSession? OffsetSession;
        public object? OffsetQuery;
        public CancellationToken OffsetToken;
        public IFerretSession? CursorSession;
        public object? CursorQuery;
        public CancellationToken CursorToken;

        public object? OffsetResultToReturn;
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

    [Fact]
    public void Implements_IFerretQueryService()
    {
        var svc = new FerretCoreQueryService(new FakeEngine(), new FakeSession());
        svc.Should().BeAssignableTo<IFerretQueryService>();
    }

    [Fact]
    public async Task SearchOffsetAsync_forwards_query_and_session_and_returns_engine_result()
    {
        var session = new FakeSession();
        var expected = new OffsetResult<Product> { Items = [] };
        var engine = new FakeEngine { OffsetResultToReturn = expected };
        var svc = new FerretCoreQueryService(engine, session);

        var query = new PagedQuery<Product, int> { Limit = 7 };
        using var cts = new CancellationTokenSource();

        var result = await svc.SearchOffsetAsync(query, cts.Token);

        result.Should().BeSameAs(expected);
        engine.OffsetSession.Should().BeSameAs(session);
        engine.OffsetQuery.Should().BeSameAs(query);
        engine.OffsetToken.Should().Be(cts.Token);
    }

    [Fact]
    public async Task SearchCursorAsync_forwards_query_and_session_and_returns_engine_result()
    {
        var session = new FakeSession();
        var expected = new CursorResult<Product> { Items = [] };
        var engine = new FakeEngine { CursorResultToReturn = expected };
        var svc = new FerretCoreQueryService(engine, session);

        var query = new PagedQuery<Product, int> { Mode = PaginationMode.Cursor, Limit = 9 };
        using var cts = new CancellationTokenSource();

        var result = await svc.SearchCursorAsync(query, cts.Token);

        result.Should().BeSameAs(expected);
        engine.CursorSession.Should().BeSameAs(session);
        engine.CursorQuery.Should().BeSameAs(query);
        engine.CursorToken.Should().Be(cts.Token);
    }
}
