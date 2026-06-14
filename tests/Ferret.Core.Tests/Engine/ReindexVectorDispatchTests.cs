using System.Data.Common;
using Ferret.Abstractions.Attributes;
using Ferret.Abstractions.Naming;
using Ferret.Abstractions.Search;
using Ferret.Abstractions.Session;
using Ferret.Abstractions.Sql;
using Ferret.Abstractions.Hydration;
using Ferret.Core.Backends.FullText;
using Ferret.Core.Backends.Vector;
using Ferret.Core.Configuration;
using Ferret.Core.Embeddings;
using Ferret.Core.Engine;
using Ferret.Core.Sql;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ferret.Core.Tests.Engine;

public sealed class ReindexVectorDispatchTests
{
    private sealed class VectorDoc
    {
        public Guid Id { get; init; }
        [Searchable(Backend = SearchBackend.Vector, Group = "content", EmbeddingDimensions = 8)]
        public string Body { get; init; } = "";
    }

    // A session whose connection open throws a recognizable marker, so we can prove
    // the call reached the vector branch (connection open) rather than failing earlier
    // at the full-text "no full-text group" validation.
    private sealed class ThrowingSession : IFerretSession
    {
        public ISqlDialect Dialect { get; } = new PostgresDialect();
        public IEntityHydrator Hydrator => throw new NotSupportedException();
        public Task<DbConnection> OpenConnectionAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("OPEN_CONNECTION_REACHED");
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static FerretEngine BuildEngine()
    {
        var reg = EntityRegistry.Build(new[] { typeof(VectorDoc) }, new SnakeCaseNamingStrategy());
        var fullText = new FullTextSearchBackend(new PostgresDialect(), new FullTextOptions());
        var vector = new VectorSearchBackend(new PostgresDialect(), new VectorOptions(), new FakeEmbeddingProvider(8));
        return new FerretEngine(
            reg,
            new ISearchBackend[] { fullText, vector },
            NullLogger<FerretEngine>.Instance,
            new FerretRuntimeOptions());
    }

    [Fact]
    public async Task ReindexAsync_with_vector_group_does_not_throw_fulltext_group_error()
    {
        var engine = BuildEngine();

        var act = async () => await engine.ReindexAsync<VectorDoc>(new ThrowingSession(), "content");

        var ex = (await act.Should().ThrowAsync<Exception>()).Which;
        ex.Message.Should().NotContain("no full-text group");
        ex.Message.Should().Contain("OPEN_CONNECTION_REACHED");
    }

    [Fact]
    public async Task ReindexAsync_with_unknown_group_throws_fulltext_group_error()
    {
        var engine = BuildEngine();

        var act = async () => await engine.ReindexAsync<VectorDoc>(new ThrowingSession(), "nope");

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("no full-text group");
    }
}
