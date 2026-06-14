using Ferret.Abstractions.Attributes;
using FluentAssertions;
using Xunit;

namespace Ferret.Abstractions.Tests.Attributes;

public class SearchableEntityAttributeTests
{
    [Fact]
    public void KeyProperties_defaults_null_and_KeyProperty_defaults_Id()
    {
        var attr = new SearchableEntityAttribute();
        attr.KeyProperty.Should().Be("Id");
        attr.KeyProperties.Should().BeNull();

        var composite = new SearchableEntityAttribute { KeyProperties = new[] { "TenantId", "Id" } };
        composite.KeyProperties.Should().Equal("TenantId", "Id");
    }
}
