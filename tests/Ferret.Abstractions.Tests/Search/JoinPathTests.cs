using FluentAssertions;
using Xunit;

namespace Ferret.Abstractions.Tests.Search;

public class JoinPathTests
{
    [Fact]
    public void JoinHop_preserves_cardinality_fk_side_and_schema()
    {
        var hop = new JoinHop
        {
            TableName = "orders",
            TableAlias = "o",
            ForeignKeyColumn = "customer_id",
            EntityType = typeof(JoinPathTests),
            Cardinality = JoinCardinality.ManyToOne,
            ForeignKeyOwningSide = true,
            Schema = "sales",
        };

        hop.Cardinality.Should().Be(JoinCardinality.ManyToOne);
        hop.ForeignKeyOwningSide.Should().BeTrue();
        hop.Schema.Should().Be("sales");
    }

    [Fact]
    public void JoinPath_equality_and_depth_hold_with_new_members()
    {
        JoinHop MakeHop() => new()
        {
            TableName = "orders",
            TableAlias = "o",
            ForeignKeyColumn = "customer_id",
            EntityType = typeof(JoinPathTests),
            Cardinality = JoinCardinality.ManyToOne,
            ForeignKeyOwningSide = true,
            Schema = "sales",
        };

        var hops = new[] { MakeHop() };
        var a = new JoinPath { Hops = hops };
        var b = new JoinPath { Hops = hops };

        a.Should().Be(b);
        a.Depth.Should().Be(1);
        a.IsDirect.Should().BeFalse();
        MakeHop().Should().Be(MakeHop());
    }
}
