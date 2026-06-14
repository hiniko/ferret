using Ferret.Abstractions.Attributes;
using FluentAssertions;
using Xunit;

namespace Ferret.Abstractions.Tests.Attributes;

public class HybridBackendAttributeTests
{
    [Fact]
    public void Carries_backend_weight_threshold()
    {
        var a = new HybridBackendAttribute(SearchBackend.Vector) { Weight = 2.0, ConfidenceThreshold = 0.25 };
        a.Backend.Should().Be(SearchBackend.Vector);
        a.Weight.Should().Be(2.0);
        a.ConfidenceThreshold.Should().Be(0.25);
    }

    [Fact]
    public void Threshold_defaults_to_NaN_meaning_inherit()
    {
        var a = new HybridBackendAttribute(SearchBackend.FullText);
        double.IsNaN(a.Weight).Should().BeTrue();
        double.IsNaN(a.ConfidenceThreshold).Should().BeTrue();
    }
}
