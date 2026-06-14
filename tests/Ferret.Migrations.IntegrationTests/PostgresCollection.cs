using Ferret.Core.IntegrationTests.Fixtures;
using Xunit;

namespace Ferret.Migrations.IntegrationTests;

[CollectionDefinition("postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture> { }
