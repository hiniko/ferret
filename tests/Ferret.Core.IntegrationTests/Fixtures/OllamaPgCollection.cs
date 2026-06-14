using Xunit;

namespace Ferret.Core.IntegrationTests.Fixtures;

[CollectionDefinition("ollama+pgvector")]
public sealed class OllamaPgCollection
    : ICollectionFixture<OllamaFixture>, ICollectionFixture<PgVectorFixture> { }
