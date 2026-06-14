using System.Reflection;
using Ferret.Abstractions;
using FluentAssertions;
using Xunit;

namespace Ferret.Abstractions.Tests.Naming;

public class NamingStrategyTests
{
    private sealed class Product
    {
        public string DisplayName { get; init; } = "";
        public string ABBR { get; init; } = "";
    }

    [Theory]
    [InlineData(typeof(Product), "products")]
    public void SnakeCase_pluralises_table_names(Type t, string expected)
    {
        new SnakeCaseNamingStrategy().TableName(t).Should().Be(expected);
    }

    [Theory]
    [InlineData(nameof(Product.DisplayName), "display_name")]
    [InlineData(nameof(Product.ABBR), "abbr")]
    public void SnakeCase_lowers_and_underscores_columns(string prop, string expected)
    {
        var p = typeof(Product).GetProperty(prop)!;
        new SnakeCaseNamingStrategy().ColumnName(p).Should().Be(expected);
    }

    [Fact]
    public void Identity_returns_clr_names_verbatim()
    {
        var s = new IdentityNamingStrategy();
        s.TableName(typeof(Product)).Should().Be("Product");
        s.ColumnName(typeof(Product).GetProperty(nameof(Product.DisplayName))!).Should().Be("DisplayName");
    }
}
