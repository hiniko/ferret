using Ferret.Core.Backends.Hybrid;
using FluentAssertions;
using Xunit;

namespace Ferret.Core.Tests.Backends.Hybrid;

public class HybridOptionsTests
{
    [Fact]
    public void Defaults_are_sensible()
    {
        var o = new HybridOptions();
        o.RrfK.Should().Be(60);
        o.CandidateDepth.Should().Be(5);
        o.DefaultWeight.Should().Be(1.0);
        o.DefaultConfidenceThreshold.Should().BeNull();
        o.MaxSearchCursorOffset.Should().Be(200);
    }
}
