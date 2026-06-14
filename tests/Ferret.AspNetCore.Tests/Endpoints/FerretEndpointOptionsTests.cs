using Ferret.AspNetCore;
using FluentAssertions;
using Xunit;

namespace Ferret.AspNetCore.Tests.Endpoints;

public class FerretEndpointOptionsTests
{
    [Fact]
    public void Defaults_are_offset_pagination_and_nulls()
    {
        var options = new FerretEndpointOptions();

        options.Pagination.Should().Be(FerretEndpointPaginationMode.Offset);
        options.DefaultLimit.Should().BeNull();
        options.MaxLimit.Should().BeNull();
        options.Tag.Should().BeNull();
        options.Summary.Should().BeNull();
    }

    [Fact]
    public void Properties_are_settable()
    {
        var options = new FerretEndpointOptions
        {
            Pagination = FerretEndpointPaginationMode.Cursor,
            DefaultLimit = 25,
            MaxLimit = 100,
            Tag = "Products",
            Summary = "List products",
        };

        options.Pagination.Should().Be(FerretEndpointPaginationMode.Cursor);
        options.DefaultLimit.Should().Be(25);
        options.MaxLimit.Should().Be(100);
        options.Tag.Should().Be("Products");
        options.Summary.Should().Be("List products");
    }
}
