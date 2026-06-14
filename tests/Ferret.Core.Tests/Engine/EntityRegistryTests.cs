using Ferret.Abstractions;
using FluentAssertions;
using Xunit;

namespace Ferret.Core.Tests.Engine;

public class EntityRegistryTests
{
    [SearchableEntity]
    private sealed class Product : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }
        [Searchable] public string Name { get; init; } = "";
        [SearchJoin(Depth = 1)] public ICollection<Variant>? Variants { get; init; }
    }

    private sealed class Variant : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }
        [Searchable] public string Sku { get; init; } = "";
    }

    [Fact]
    public void Registers_entity_with_one_hop_searchable()
    {
        var reg = EntityRegistry.Build(new[] { typeof(Product) }, new SnakeCaseNamingStrategy());
        var model = reg.Get<Product>();
        model.TableName.Should().Be("products");
        model.KeyColumnName.Should().Be("id");
        model.SearchableProperties.Should().HaveCount(2);                    // Name (direct) + Variant.Sku (1-hop)
        model.SearchableProperties.Single(p => p.Property.Name == "Sku")
            .JoinPath.Depth.Should().Be(1);
    }

    [SearchableEntity]
    private sealed class A : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }
        [SearchJoin(Depth = 3)] public ICollection<B>? Bs { get; init; }
    }

    private sealed class B : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }
        [SearchJoin(Depth = 3)] public ICollection<C>? Cs { get; init; }
    }

    private sealed class C : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }
        [Searchable] public string Name { get; init; } = "";
    }

    [Fact]
    public void Throws_when_aggregate_hop_budget_exceeds_five()
    {
        Action act = () => EntityRegistry.Build(new[] { typeof(A) }, new SnakeCaseNamingStrategy());
        act.Should().Throw<InvalidOperationException>().WithMessage("*hop budget*5*");
    }
}
