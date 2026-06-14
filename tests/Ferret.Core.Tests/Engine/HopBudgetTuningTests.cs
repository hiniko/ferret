using Ferret.Abstractions;
using FluentAssertions;
using Xunit;

namespace Ferret.Core.Tests.Engine;

// Pins the tuned HopBudget chosen in docs/superpowers/specs/2026-05-31-searchjoin-bench-findings.md.
// The findings reaffirm a budget of 5; this test fails if the constant silently changes without
// updating the findings doc reference.
public class HopBudgetTuningTests
{
    private const int ChosenHopBudget = 5;

    [SearchableEntity]
    private sealed class AtBudget : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }
        [SearchJoin(Depth = ChosenHopBudget)] public ICollection<AtBudgetLeaf>? Leaves { get; init; }
    }

    private sealed class AtBudgetLeaf : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }
        [Searchable] public string Name { get; init; } = "";
    }

    [SearchableEntity]
    private sealed class OverBudget : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }
        [SearchJoin(Depth = ChosenHopBudget + 1)] public ICollection<OverBudgetLeaf>? Leaves { get; init; }
    }

    private sealed class OverBudgetLeaf : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }
        [Searchable] public string Name { get; init; } = "";
    }

    [Fact]
    public void Allows_chain_at_chosen_budget()
    {
        Action act = () => EntityRegistry.Build(new[] { typeof(AtBudget) }, new SnakeCaseNamingStrategy());
        act.Should().NotThrow();
    }

    [Fact]
    public void Throws_at_chosen_budget_plus_one()
    {
        Action act = () => EntityRegistry.Build(new[] { typeof(OverBudget) }, new SnakeCaseNamingStrategy());
        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*hop budget*{ChosenHopBudget}*");
    }
}
