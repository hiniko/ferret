using System.Reflection;
using FluentAssertions;
using Xunit;

namespace Ferret.Abstractions.Tests;

public class SearchableAttributeTests
{
    private sealed class Sample
    {
        public string Name { get; init; } = "";
    }

    [Fact]
    public void PreviousGroup_defaults_to_null_and_round_trips()
    {
        new SearchableAttribute().PreviousGroup.Should().BeNull();

        new SearchableAttribute { PreviousGroup = "old" }.PreviousGroup.Should().Be("old");

        var info = new SearchablePropertyInfo
        {
            Property = typeof(Sample).GetProperty(nameof(Sample.Name))!,
            Backend = SearchBackend.FullText,
            Weight = 1.0f,
            ColumnName = "name",
            OwnerTableName = "sample",
        };
        info.PreviousGroup.Should().BeNull();
    }
}
