using Ferret.Abstractions;
using FluentAssertions;
using Xunit;

namespace Ferret.Core.Tests.Engine;

public sealed class EntityModelBuilderSearchJoinTests
{
    [SearchableEntity]
    private sealed class Order : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }
        public Guid CustomerId { get; init; }

        [Searchable] public string Reference { get; init; } = "";

        [SearchJoin]
        public Customer? Customer { get; init; }
    }

    [SearchableEntity(Schema = "sales")]
    private sealed class Customer : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }
        [Searchable] public string Name { get; init; } = "";
    }

    [SearchableEntity]
    private sealed class OrderWithFkOverride : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }
        public Guid BuyerKey { get; init; }

        [Searchable] public string Reference { get; init; } = "";

        [SearchJoin(ForeignKey = "buyer_key")]
        public Customer? Buyer { get; init; }
    }

    [SearchableEntity]
    private sealed class Parent : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }
        [Searchable] public string Title { get; init; } = "";

        [SearchJoin]
        public IReadOnlyList<Child> Children { get; init; } = [];
    }

    private sealed class Child : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }
        public Guid ParentId { get; init; }
        [Searchable] public string Note { get; init; } = "";
    }

    [SearchableEntity]
    private sealed class Invoice : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }
        public Guid OrderRefId { get; init; }
        [Searchable] public string Number { get; init; } = "";

        // N:1 reference, then the referenced order has a 1:N collection.
        [SearchJoin]
        public OrderWithChildren? Order { get; init; }
    }

    [SearchableEntity]
    private sealed class OrderWithChildren : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }
        [Searchable] public string Reference { get; init; } = "";

        [SearchJoin]
        public IReadOnlyList<Line> Lines { get; init; } = [];
    }

    private sealed class Line : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }
        public Guid OrderWithChildrenId { get; init; }
        [Searchable] public string Sku { get; init; } = "";
    }

    [SearchableEntity]
    private sealed class DeepRef : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }
        [SearchJoin(Depth = 6)]
        public Customer? Customer { get; init; }
    }

    [SearchableEntity(KeyProperty = "Code")]
    private sealed class CustomKeyParent
    {
        public Guid Code { get; init; }

        [Searchable] public string Title { get; init; } = "";

        // Owner has no 'Id' property and the collection join has no ForeignKey override → unresolvable.
        [SearchJoin]
        public IReadOnlyList<Child> Children { get; init; } = [];
    }

    [SearchableEntity(Schema = "sales")]
    private sealed class CustomerFullText : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }
        [Searchable(Backend = SearchBackend.FullText, FullTextConfig = "english", Group = "blurb")]
        public string Name { get; init; } = "";
    }

    [SearchableEntity]
    private sealed class OrderWithConflictingJoinedConfig : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }
        public Guid CustomerId { get; init; }

        [Searchable(Backend = SearchBackend.FullText, FullTextConfig = "simple", Group = "blurb")]
        public string Reference { get; init; } = "";

        // Joined fulltext prop in the same group declares a conflicting config.
        [SearchJoin]
        public CustomerFullText? Customer { get; init; }
    }

    [SearchableEntity]
    private sealed class OrderWithCompatibleJoinedConfig : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }
        public Guid CustomerId { get; init; }

        [Searchable(Backend = SearchBackend.FullText, FullTextConfig = "english", Group = "blurb")]
        public string Reference { get; init; } = "";

        // Joined fulltext prop in the same group with a matching config.
        [SearchJoin]
        public CustomerFullText? Customer { get; init; }
    }

    [SearchableEntity(KeyProperty = "Code")]
    private sealed class Warehouse
    {
        public Guid Code { get; init; }
        [Searchable] public string Name { get; init; } = "";
    }

    [SearchableEntity]
    private sealed class Shipment : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }
        public Guid WarehouseId { get; init; }
        [Searchable] public string Reference { get; init; } = "";

        // N:1 reference to an entity whose key column is not "id".
        [SearchJoin]
        public Warehouse? Warehouse { get; init; }
    }

    [SearchableEntity(KeyProperties = ["RegionId", "Code"])]
    private sealed class CompositeKeyWarehouse
    {
        public Guid RegionId { get; init; }
        public Guid Code { get; init; }
        [Searchable] public string Name { get; init; } = "";
    }

    [SearchableEntity]
    private sealed class ShipmentToComposite : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }
        public Guid WarehouseId { get; init; }
        [Searchable] public string Reference { get; init; } = "";

        [SearchJoin]
        public CompositeKeyWarehouse? Warehouse { get; init; }
    }

    private static EntityModel Build(Type t) =>
        EntityModelBuilder.Build(t, new SnakeCaseNamingStrategy());

    [Fact]
    public void Reference_nav_to_non_id_key_captures_referenced_key_column()
    {
        var model = Build(typeof(Shipment));

        var joined = model.SearchableProperties.Single(s => s.Property.Name == "Name");
        var hop = joined.JoinPath.Hops[0];

        hop.Cardinality.Should().Be(JoinCardinality.ManyToOne);
        hop.ForeignKeyColumn.Should().Be("warehouse_id");
        hop.ReferencedKeyColumn.Should().Be("code");
    }

    [Fact]
    public void Reference_nav_to_composite_key_throws()
    {
        Action act = () => Build(typeof(ShipmentToComposite));
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*composite primary key*");
    }

    [Fact]
    public void Reference_nav_produces_many_to_one_hop_with_fk_on_owner_side()
    {
        var model = Build(typeof(Order));

        var joined = model.SearchableProperties.Single(s => s.Property.Name == "Name");

        joined.JoinPath.Hops.Should().HaveCount(1);
        var hop = joined.JoinPath.Hops[0];
        hop.Cardinality.Should().Be(JoinCardinality.ManyToOne);
        hop.ForeignKeyOwningSide.Should().BeTrue();
        hop.ForeignKeyColumn.Should().Be("customer_id");
        hop.TableName.Should().Be("customers");
        hop.Schema.Should().Be("sales");
    }

    [Fact]
    public void Reference_nav_respects_foreign_key_override()
    {
        var model = Build(typeof(OrderWithFkOverride));

        var joined = model.SearchableProperties.Single(s => s.Property.Name == "Name");
        var hop = joined.JoinPath.Hops[0];

        hop.Cardinality.Should().Be(JoinCardinality.ManyToOne);
        hop.ForeignKeyColumn.Should().Be("buyer_key");
        hop.ForeignKeyOwningSide.Should().BeTrue();
    }

    [Fact]
    public void Collection_nav_discovery_is_unchanged_one_to_many_on_related_side()
    {
        var model = Build(typeof(Parent));

        var joined = model.SearchableProperties.Single(s => s.Property.Name == "Note");
        joined.JoinPath.Hops.Should().HaveCount(1);

        var hop = joined.JoinPath.Hops[0];
        hop.Cardinality.Should().Be(JoinCardinality.OneToMany);
        hop.ForeignKeyOwningSide.Should().BeFalse();
        hop.TableName.Should().Be(new SnakeCaseNamingStrategy().TableName(typeof(Child)));
    }

    [Fact]
    public void Mixed_many_to_one_then_one_to_many_chain_composes()
    {
        var model = Build(typeof(Invoice));

        var leaf = model.SearchableProperties.Single(s => s.Property.Name == "Sku");
        leaf.JoinPath.Hops.Should().HaveCount(2);

        leaf.JoinPath.Hops[0].Cardinality.Should().Be(JoinCardinality.ManyToOne);
        leaf.JoinPath.Hops[0].ForeignKeyOwningSide.Should().BeTrue();

        leaf.JoinPath.Hops[1].Cardinality.Should().Be(JoinCardinality.OneToMany);
        leaf.JoinPath.Hops[1].ForeignKeyOwningSide.Should().BeFalse();
    }

    [Fact]
    public void Reference_hop_budget_overflow_throws()
    {
        Action act = () => Build(typeof(DeepRef));
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*hop budget*");
    }

    [Fact]
    public void Unresolvable_join_fk_throws_naming_entity_and_property()
    {
        Action act = () => Build(typeof(CustomKeyParent));
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*CustomKeyParent*Children*");
    }

    [Fact]
    public void Joined_props_with_conflicting_fulltext_config_throws()
    {
        Action act = () => Build(typeof(OrderWithConflictingJoinedConfig));
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*conflicting FullTextConfig*");
    }

    [Fact]
    public void Valid_mixed_group_with_joined_props_does_not_throw()
    {
        Action act = () => Build(typeof(OrderWithCompatibleJoinedConfig));
        act.Should().NotThrow();
    }
}
