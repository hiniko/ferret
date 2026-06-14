using Ferret.Core.Engine.Reindex;
using FluentAssertions;
using Xunit;

namespace Ferret.Core.Tests;

public sealed class ReindexLockKeyTests
{
    [Fact]
    public void For_produces_prefixed_entity_and_group_key()
    {
        var key = ReindexLockKey.For("Document", "default");

        key.Should().Be("ferret-reindex:Document:default");
    }
}
