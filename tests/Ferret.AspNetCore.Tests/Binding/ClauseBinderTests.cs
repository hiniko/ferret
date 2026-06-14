using Ferret.Abstractions;
using Ferret.AspNetCore.Binding;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Ferret.AspNetCore.Tests.Binding;

public class ClauseBinderTests
{
    [Fact]
    public async Task FilterClauseListBinder_parses_repeated_compact_clauses()
    {
        var ctx = MakeBindingContext("filter", new[] { "name:eq:alpha", "price:gt:10" });
        await new FilterClauseListBinder().BindModelAsync(ctx);

        var bound = (IReadOnlyList<FilterClause>)ctx.Result.Model!;
        bound.Should().HaveCount(2);
        bound[0].Should().BeEquivalentTo(new FilterClause { Field = "name", Operator = FilterOperator.Equals, Value = "alpha" });
        bound[1].Should().BeEquivalentTo(new FilterClause { Field = "price", Operator = FilterOperator.GreaterThan, Value = "10" });
    }

    [Theory]
    [InlineData("neq", FilterOperator.NotEquals)]
    [InlineData("gte", FilterOperator.GreaterThanOrEqual)]
    [InlineData("lte", FilterOperator.LessThanOrEqual)]
    [InlineData("in", FilterOperator.In)]
    public async Task FilterClauseListBinder_parses_extended_operators(string wire, FilterOperator expected)
    {
        var ctx = MakeBindingContext("filter", new[] { $"field:{wire}:val" });
        await new FilterClauseListBinder().BindModelAsync(ctx);

        var bound = (IReadOnlyList<FilterClause>)ctx.Result.Model!;
        bound.Should().ContainSingle().Which.Operator.Should().Be(expected);
    }

    [Fact]
    public async Task FilterClauseListBinder_skips_unknown_operator()
    {
        var ctx = MakeBindingContext("filter", new[] { "name:weird:alpha" });
        await new FilterClauseListBinder().BindModelAsync(ctx);

        var bound = (IReadOnlyList<FilterClause>)ctx.Result.Model!;
        bound.Should().BeEmpty();
    }

    [Fact]
    public async Task SortClauseListBinder_defaults_to_ascending()
    {
        var ctx = MakeBindingContext("sort", new[] { "name", "created_at:desc" });
        await new SortClauseListBinder().BindModelAsync(ctx);

        var bound = (IReadOnlyList<SortClause>)ctx.Result.Model!;
        bound.Should().HaveCount(2);
        bound[0].Direction.Should().Be(SortDirection.Ascending);
        bound[1].Direction.Should().Be(SortDirection.Descending);
    }

    private static DefaultModelBindingContext MakeBindingContext(string name, string[] values)
    {
        var provider = new SimpleValueProvider(name, values);
        return new DefaultModelBindingContext
        {
            ModelName = name,
            ValueProvider = provider,
            ModelMetadata = new EmptyModelMetadataProvider().GetMetadataForType(typeof(IReadOnlyList<FilterClause>)),
            ModelState = new ModelStateDictionary(),
        };
    }

    private sealed class SimpleValueProvider : IValueProvider
    {
        private readonly string _name;
        private readonly string[] _values;
        public SimpleValueProvider(string name, string[] values) { _name = name; _values = values; }
        public bool ContainsPrefix(string prefix) => prefix == _name;
        public ValueProviderResult GetValue(string key) =>
            key == _name ? new ValueProviderResult(new StringValues(_values)) : ValueProviderResult.None;
    }
}
