using Ferret.Core.IntegrationTests.Fixtures;
using Xunit;

namespace Ferret.Dev.IntegrationTests;

// xUnit collection definitions are per-assembly. The fixtures themselves are reused
// from Ferret.Core.IntegrationTests via project reference; these re-declare the
// collections so the moved tests resolve them inside this assembly.

[CollectionDefinition("ollama")]
public sealed class OllamaCollection : ICollectionFixture<OllamaFixture> { }

[CollectionDefinition("pgvector")]
public sealed class PgVectorCollection : ICollectionFixture<PgVectorFixture> { }

[CollectionDefinition("ollama+pgvector")]
public sealed class OllamaPgCollection
    : ICollectionFixture<OllamaFixture>, ICollectionFixture<PgVectorFixture> { }
