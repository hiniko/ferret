using FluentAssertions;
using Xunit;

namespace Ferret.Abstractions.Tests.Models;

public class PagedQueryBackendTests
{
    private sealed class E { }

    [Fact]
    public void Backend_defaults_null_and_round_trips()
    {
        new PagedQuery<E, Guid>().Backend.Should().BeNull();
        new PagedQuery<E, Guid> { Backend = SearchBackend.Vector }.Backend.Should().Be(SearchBackend.Vector);
    }
}
