using Ferret.Abstractions.Attributes;
using Ferret.Abstractions.Naming;
using Ferret.Abstractions.Search;
using Ferret.Core.Backends.Trigram;
using Ferret.Core.Backends.Vector;
using Ferret.Core.Configuration;
using Ferret.Core.Embeddings;
using Ferret.Core.Engine;
using Ferret.Core.Sql;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ferret.Core.Tests.Engine;

public sealed class ResolveBackendVectorTests
{
    private sealed class VectorOnly
    {
        public Guid Id { get; init; }
        [Searchable(Backend = SearchBackend.Vector, Group = "content", EmbeddingDimensions = 8)]
        public string Body { get; init; } = "";
    }

    [Fact]
    public void Vector_entity_resolves_vector_backend()
    {
        var reg = EntityRegistry.Build(new[] { typeof(VectorOnly) }, new SnakeCaseNamingStrategy());

        var trigram = new TrigramSearchBackend(new PostgresDialect(), new TrigramOptions());
        var vector = new VectorSearchBackend(new PostgresDialect(), new VectorOptions(), new FakeEmbeddingProvider(8));

        var engine = new FerretEngine(
            reg,
            new ISearchBackend[] { trigram, vector },
            NullLogger<FerretEngine>.Instance,
            new FerretRuntimeOptions());

        engine.ResolveBackend(reg.Get<VectorOnly>()).Name.Should().Be("vector");
    }
}
