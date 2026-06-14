using FluentAssertions;
using Xunit;

namespace Ferret.Core.Tests.Engine;

public class EntityModelKeyTests
{
    private static EntityModel BuildSingleKeyModel() => new()
    {
        ClrType = typeof(object),
        TableName = "products",
        Key = [new KeyPart { PropertyName = "Id", ColumnName = "id", ClrType = typeof(Guid) }],
        ColumnByPropertyName = new Dictionary<string, string> { ["Id"] = "id" },
        ClrTypeByPropertyName = new Dictionary<string, Type> { ["Id"] = typeof(Guid) },
        SearchableProperties = [],
        Filterable = new Dictionary<string, FilterableAttribute>(),
        Sortable = new HashSet<string>(),
    };

    [Fact]
    public void SingleKey_exposes_one_KeyPart_and_IsComposite_false()
    {
        var model = BuildSingleKeyModel();

        model.Key.Should().ContainSingle();
        model.Key[0].PropertyName.Should().Be("Id");
        model.Key[0].ColumnName.Should().Be("id");
        model.Key[0].ClrType.Should().Be(typeof(Guid));
        model.IsComposite.Should().BeFalse();
    }
}
