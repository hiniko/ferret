using Ferret.Abstractions;
using Ferret.AspNetCore;
using Ferret.AspNetCore.Binding;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Ferret.AspNetCore.Tests.Binding;

public class QueryStringQueryBinderTests
{
    [Fact]
    public void BindOffset_maps_all_scalar_params()
    {
        var query = new FakeQueryCollection(new Dictionary<string, StringValues>
        {
            ["q"] = "widge",
            ["fields"] = new StringValues(new[] { "Name", "Sku" }),
            ["match_info"] = "true",
            ["limit"] = "50",
            ["page"] = "2",
            ["count"] = "false",
        });

        var result = QueryStringQueryBinder.BindOffset(query);

        result.Q.Should().Be("widge");
        result.Fields.Should().Equal("Name", "Sku");
        result.MatchInfo.Should().BeTrue();
        result.Limit.Should().Be(50);
        result.Page.Should().Be(2);
        result.Count.Should().BeFalse();
    }

    [Fact]
    public void BindOffset_maps_repeated_filter_and_sort_via_clause_parsing()
    {
        var query = new FakeQueryCollection(new Dictionary<string, StringValues>
        {
            ["filter"] = new StringValues(new[] { "Price:lt:50", "Name:eq:widget" }),
            ["sort"] = new StringValues(new[] { "Name", "Price:desc" }),
        });

        var result = QueryStringQueryBinder.BindOffset(query);

        result.Filter.Should().HaveCount(2);
        result.Filter[0].Field.Should().Be("Price");
        result.Filter[0].Operator.Should().Be(FilterOperator.LessThan);
        result.Sort.Should().HaveCount(2);
        result.Sort[1].Field.Should().Be("Price");
        result.Sort[1].Direction.Should().Be(SortDirection.Descending);
    }

    [Fact]
    public void BindOffset_uses_defaults_for_absent_params()
    {
        var query = new FakeQueryCollection(new Dictionary<string, StringValues>());

        var result = QueryStringQueryBinder.BindOffset(query);

        result.Q.Should().BeNull();
        result.Fields.Should().BeEmpty();
        result.MatchInfo.Should().BeFalse();
        result.Limit.Should().BeNull();
        result.Page.Should().BeNull();
        result.Count.Should().BeTrue();
        result.Filter.Should().BeEmpty();
        result.Sort.Should().BeEmpty();
    }

    [Fact]
    public void BindCursor_maps_after()
    {
        var query = new FakeQueryCollection(new Dictionary<string, StringValues>
        {
            ["after"] = "tok",
            ["limit"] = "10",
        });

        var result = QueryStringQueryBinder.BindCursor(query);

        result.After.Should().Be("tok");
        result.Before.Should().BeNull();
        result.Limit.Should().Be(10);
    }

    [Fact]
    public void BindCursor_maps_before()
    {
        var query = new FakeQueryCollection(new Dictionary<string, StringValues>
        {
            ["before"] = "tok",
        });

        var result = QueryStringQueryBinder.BindCursor(query);

        result.Before.Should().Be("tok");
        result.After.Should().BeNull();
    }

    [Fact]
    public void BindCursor_uses_defaults_for_absent_params()
    {
        var query = new FakeQueryCollection(new Dictionary<string, StringValues>());

        var result = QueryStringQueryBinder.BindCursor(query);

        result.After.Should().BeNull();
        result.Before.Should().BeNull();
        result.Limit.Should().BeNull();
        result.Fields.Should().BeEmpty();
        result.Filter.Should().BeEmpty();
        result.Sort.Should().BeEmpty();
    }

    private sealed class FakeQueryCollection : IQueryCollection
    {
        private readonly Dictionary<string, StringValues> _store;

        public FakeQueryCollection(Dictionary<string, StringValues> store) => _store = store;

        public StringValues this[string key] => _store.TryGetValue(key, out var v) ? v : StringValues.Empty;

        public int Count => _store.Count;

        public ICollection<string> Keys => _store.Keys;

        public bool ContainsKey(string key) => _store.ContainsKey(key);

        public bool TryGetValue(string key, out StringValues value) => _store.TryGetValue(key, out value);

        public IEnumerator<KeyValuePair<string, StringValues>> GetEnumerator() => _store.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _store.GetEnumerator();
    }
}
