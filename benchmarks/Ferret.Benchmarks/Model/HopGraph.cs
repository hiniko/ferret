using Ferret.Abstractions.Attributes;

namespace Ferret.Benchmarks.Model;

public static class HopGraph
{
    public static Type EntityTypeForDepth(int depth) => depth switch
    {
        1 => typeof(Owner1),
        2 => typeof(Owner2),
        3 => typeof(Owner3),
        4 => typeof(Owner4),
        5 => typeof(Owner5),
        _ => throw new ArgumentOutOfRangeException(
            nameof(depth), depth, "Hop depth must be between 1 and 5."),
    };

    [SearchableEntity(Table = "owner1")]
    public sealed class Owner1
    {
        public Guid Id { get; init; }
        [Searchable] public string Name { get; init; } = "";
        [SearchJoin(ForeignKey = "owner_id")]
        public IReadOnlyList<H1> Hop1 { get; init; } = [];
    }

    [SearchableEntity(Table = "owner2")]
    public sealed class Owner2
    {
        public Guid Id { get; init; }
        [Searchable] public string Name { get; init; } = "";
        [SearchJoin(ForeignKey = "owner_id")]
        public IReadOnlyList<H1WithChild> Hop1 { get; init; } = [];
    }

    [SearchableEntity(Table = "owner3")]
    public sealed class Owner3
    {
        public Guid Id { get; init; }
        [Searchable] public string Name { get; init; } = "";
        [SearchJoin(ForeignKey = "owner_id")]
        public IReadOnlyList<H2WithChild> Hop1 { get; init; } = [];
    }

    [SearchableEntity(Table = "owner4")]
    public sealed class Owner4
    {
        public Guid Id { get; init; }
        [Searchable] public string Name { get; init; } = "";
        [SearchJoin(ForeignKey = "owner_id")]
        public IReadOnlyList<H3WithChild> Hop1 { get; init; } = [];
    }

    [SearchableEntity(Table = "owner5")]
    public sealed class Owner5
    {
        public Guid Id { get; init; }
        [Searchable] public string Name { get; init; } = "";
        [SearchJoin(ForeignKey = "owner_id")]
        public IReadOnlyList<H4WithChild> Hop1 { get; init; } = [];
    }

    public sealed class H1
    {
        public Guid Id { get; init; }
        public Guid OwnerId { get; init; }
        [Searchable] public string Label { get; init; } = "";
    }

    public sealed class H1WithChild
    {
        public Guid Id { get; init; }
        public Guid OwnerId { get; init; }
        [Searchable] public string Label { get; init; } = "";
        [SearchJoin(ForeignKey = "parent_id")]
        public IReadOnlyList<H1> Children { get; init; } = [];
    }

    public sealed class H2WithChild
    {
        public Guid Id { get; init; }
        public Guid OwnerId { get; init; }
        [Searchable] public string Label { get; init; } = "";
        [SearchJoin(ForeignKey = "parent_id")]
        public IReadOnlyList<H1WithChild> Children { get; init; } = [];
    }

    public sealed class H3WithChild
    {
        public Guid Id { get; init; }
        public Guid OwnerId { get; init; }
        [Searchable] public string Label { get; init; } = "";
        [SearchJoin(ForeignKey = "parent_id")]
        public IReadOnlyList<H2WithChild> Children { get; init; } = [];
    }

    public sealed class H4WithChild
    {
        public Guid Id { get; init; }
        public Guid OwnerId { get; init; }
        [Searchable] public string Label { get; init; } = "";
        [SearchJoin(ForeignKey = "parent_id")]
        public IReadOnlyList<H3WithChild> Children { get; init; } = [];
    }
}
