using System.Threading;
using System.Threading.Tasks;
using Ferret.Abstractions.Querying;
using FluentAssertions;
using Xunit;

namespace Ferret.Abstractions.Tests.Querying;

public class IFerretQueryServiceTests
{
    [Fact]
    public void IFerretQueryService_is_interface()
    {
        typeof(IFerretQueryService).IsInterface.Should().BeTrue();
    }

    [Fact]
    public void SearchOffsetAsync_is_generic_over_T_and_TKey()
    {
        var m = typeof(IFerretQueryService).GetMethod(nameof(IFerretQueryService.SearchOffsetAsync));
        m.Should().NotBeNull();
        m!.IsGenericMethodDefinition.Should().BeTrue();
        m.GetGenericArguments().Should().HaveCount(2);
    }

    [Fact]
    public void SearchCursorAsync_is_generic_over_T_and_TKey()
    {
        var m = typeof(IFerretQueryService).GetMethod(nameof(IFerretQueryService.SearchCursorAsync));
        m.Should().NotBeNull();
        m!.IsGenericMethodDefinition.Should().BeTrue();
        m.GetGenericArguments().Should().HaveCount(2);
    }

    [Fact]
    public void T_carries_reference_type_constraint()
    {
        var m = typeof(IFerretQueryService).GetMethod(nameof(IFerretQueryService.SearchOffsetAsync))!;
        var t = m.GetGenericArguments()[0];

        (t.GenericParameterAttributes & System.Reflection.GenericParameterAttributes.ReferenceTypeConstraint)
            .Should().Be(System.Reflection.GenericParameterAttributes.ReferenceTypeConstraint);
    }

    [Fact]
    public void FakeImplementation_satisfies_the_contract()
    {
        IFerretQueryService svc = new FakeQueryService();
        svc.Should().NotBeNull();
    }

    private sealed class FakeQueryService : IFerretQueryService
    {
        public Task<OffsetResult<T>> SearchOffsetAsync<T, TKey>(
            PagedQuery<T, TKey> query,
            CancellationToken ct = default)
            where T : class
            where TKey : notnull
            => Task.FromResult(new OffsetResult<T> { Items = [] });

        public Task<CursorResult<T>> SearchCursorAsync<T, TKey>(
            PagedQuery<T, TKey> query,
            CancellationToken ct = default)
            where T : class
            where TKey : notnull
            => Task.FromResult(new CursorResult<T> { Items = [] });
    }
}
