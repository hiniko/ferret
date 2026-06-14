using Ferret.Abstractions;
using FluentAssertions;
using Xunit;

namespace Ferret.Core.Tests.Engine;

public sealed class EntityModelBuilderKeyTests
{
    [SearchableEntity(KeyProperties = new[] { "TenantId", "OrderId" })]
    private sealed class CompositeKeyEntity
    {
        public Guid TenantId { get; init; }
        public long OrderId { get; init; }
        public string Name { get; init; } = "";
    }

    [SearchableEntity(KeyProperty = "Sku")]
    private sealed class ShorthandKeyEntity
    {
        public string Sku { get; init; } = "";
        public string Name { get; init; } = "";
    }

    [SearchableEntity(KeyProperty = "Sku", KeyProperties = new[] { "TenantId", "OrderId" })]
    private sealed class BothSetEntity
    {
        public Guid TenantId { get; init; }
        public long OrderId { get; init; }
        public string Sku { get; init; } = "";
    }

    [SearchableEntity(KeyProperties = new[] { "TenantId", "Missing" })]
    private sealed class MissingKeyEntity
    {
        public Guid TenantId { get; init; }
    }

    [SearchableEntity]
    private sealed class DefaultKeyEntity
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = "";
    }

    [Fact]
    public void KeyProperties_resolves_columns_in_declaration_order()
    {
        var model = EntityModelBuilder.Build(typeof(CompositeKeyEntity), new SnakeCaseNamingStrategy());

        model.Key.Should().HaveCount(2);
        model.Key[0].PropertyName.Should().Be("TenantId");
        model.Key[0].ColumnName.Should().Be("tenant_id");
        model.Key[0].ClrType.Should().Be(typeof(Guid));
        model.Key[1].PropertyName.Should().Be("OrderId");
        model.Key[1].ColumnName.Should().Be("order_id");
        model.Key[1].ClrType.Should().Be(typeof(long));
    }

    [Fact]
    public void KeyProperty_shorthand_yields_single_part()
    {
        var model = EntityModelBuilder.Build(typeof(ShorthandKeyEntity), new SnakeCaseNamingStrategy());

        model.Key.Should().ContainSingle();
        model.Key[0].PropertyName.Should().Be("Sku");
        model.Key[0].ColumnName.Should().Be("sku");
        model.Key[0].ClrType.Should().Be(typeof(string));
    }

    [Fact]
    public void both_set_throws()
    {
        Action act = () => EntityModelBuilder.Build(typeof(BothSetEntity), new SnakeCaseNamingStrategy());

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void missing_property_throws()
    {
        Action act = () => EntityModelBuilder.Build(typeof(MissingKeyEntity), new SnakeCaseNamingStrategy());

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void default_Id_when_unset()
    {
        var model = EntityModelBuilder.Build(typeof(DefaultKeyEntity), new SnakeCaseNamingStrategy());

        model.Key.Should().ContainSingle();
        model.Key[0].PropertyName.Should().Be("Id");
        model.Key[0].ColumnName.Should().Be("id");
    }

    [SearchableEntity]
    private sealed class EfAutoFillEntity
    {
        public Guid TenantId { get; init; }
        public long DocId { get; init; }
        public string Name { get; init; } = "";
    }

    [Fact]
    public void key_override_auto_fills_composite_key_when_attribute_unset()
    {
        var model = EntityModelBuilder.Build(
            typeof(EfAutoFillEntity), new SnakeCaseNamingStrategy(),
            fullTextDefaults: null, keyPropertyOverride: ["TenantId", "DocId"]);

        model.Key.Should().HaveCount(2);
        model.Key[0].PropertyName.Should().Be("TenantId");
        model.Key[1].PropertyName.Should().Be("DocId");
    }

    [Fact]
    public void key_override_ignored_when_attribute_names_keys()
    {
        var model = EntityModelBuilder.Build(
            typeof(CompositeKeyEntity), new SnakeCaseNamingStrategy(),
            fullTextDefaults: null, keyPropertyOverride: ["TenantId"]);

        model.Key.Should().HaveCount(2);
        model.Key[0].PropertyName.Should().Be("TenantId");
        model.Key[1].PropertyName.Should().Be("OrderId");
    }
}
