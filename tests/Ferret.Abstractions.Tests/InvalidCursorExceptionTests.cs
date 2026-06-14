using FluentAssertions;
using Xunit;

namespace Ferret.Abstractions.Tests;

public class InvalidCursorExceptionTests
{
    [Fact]
    public void lives_in_abstractions_models_namespace()
    {
        var type = typeof(Ferret.Abstractions.Models.InvalidCursorException);

        type.Should().NotBeNull();
        typeof(Exception).IsAssignableFrom(type).Should().BeTrue();

        var ex = new Ferret.Abstractions.Models.InvalidCursorException("boom");
        ex.Message.Should().Be("boom");
    }
}
