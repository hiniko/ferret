using System.Reflection;
using Ferret.Abstractions;
using FluentAssertions;
using Xunit;

namespace Ferret.Abstractions.Tests.Attributes;

public class AttributeShapeTests
{
    [SearchableEntity(Table = "products", Schema = "shop", KeyProperty = "Id")]
    [SearchIgnore("InternalCode")]
    private sealed class Sample
    {
        [SearchColumn(Name = "name_col")]
        [Searchable(Backend = SearchBackend.Trigram, Weight = 2.5f)]
        [Filterable, Sortable]
        public string Name { get; init; } = "";

        [SearchJoin(Depth = 2)]
        public ICollection<Sample>? Children { get; init; }
    }

    [Fact]
    public void FerretEntity_carries_table_and_schema_and_key()
    {
        var attr = typeof(Sample).GetCustomAttribute<SearchableEntityAttribute>();
        attr.Should().NotBeNull();
        attr!.Table.Should().Be("products");
        attr.Schema.Should().Be("shop");
        attr.KeyProperty.Should().Be("Id");
    }

    [Fact]
    public void FerretIgnore_lists_property_names()
    {
        typeof(Sample).GetCustomAttribute<SearchIgnoreAttribute>()!
            .PropertyNames.Should().ContainSingle().Which.Should().Be("InternalCode");
    }

    [Fact]
    public void Searchable_defaults_weight_to_one()
    {
        var attr = new SearchableAttribute();
        attr.Backend.Should().Be(SearchBackend.Trigram);
        attr.Weight.Should().Be(1.0f);
    }

    [Fact]
    public void Searchable_allows_multiple_per_property_for_hybrid_coverage()
    {
        var attr = typeof(SearchableAttribute).GetCustomAttribute<AttributeUsageAttribute>();
        attr!.AllowMultiple.Should().BeTrue();
    }

    [Fact]
    public void SearchJoin_default_depth_is_one()
    {
        new SearchJoinAttribute().Depth.Should().Be(1);
    }

    [Fact]
    public void FerretColumn_carries_required_name()
    {
        var prop = typeof(Sample).GetProperty(nameof(Sample.Name))!;
        prop.GetCustomAttribute<SearchColumnAttribute>()!.Name.Should().Be("name_col");
    }
}
