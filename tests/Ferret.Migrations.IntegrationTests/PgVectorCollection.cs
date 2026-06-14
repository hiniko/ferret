using Ferret.Core.IntegrationTests.Fixtures;
using Xunit;

namespace Ferret.Migrations.IntegrationTests;

[CollectionDefinition("pgvector")]
public sealed class PgVectorCollection : ICollectionFixture<PgVectorFixture> { }
