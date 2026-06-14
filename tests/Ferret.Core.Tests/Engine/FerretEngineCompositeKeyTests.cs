using System.Reflection;
using Ferret.Abstractions;
using Ferret.Abstractions.Naming;
using Ferret.Abstractions.Sql;
using Ferret.Core.Engine;
using Ferret.Core.Engine.Cursor;
using Ferret.Core.Sql;
using FluentAssertions;
using Xunit;

namespace Ferret.Core.Tests.Engine;

public sealed class FerretEngineCompositeKeyTests
{
    [SearchableEntity(KeyProperties = new[] { "TenantId", "OrderId" })]
    private sealed class CompositeKeyEntity
    {
        public Guid TenantId { get; init; }
        public long OrderId { get; init; }
        public string Name { get; init; } = "";
    }

    [SearchableEntity]
    private sealed class SingleKeyEntity
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = "";
    }

    private static EntityModel Model<T>() =>
        EntityModelBuilder.Build(typeof(T), new SnakeCaseNamingStrategy());

    private static EntityMetadata Meta<T>() =>
        EntityMetadata.From(Model<T>(), new PostgresDialect());

    [Fact]
    public void EncodeAnchor_writes_both_primary_key_parts_for_composite_key()
    {
        var model = Model<CompositeKeyEntity>();
        var tenant = Guid.NewGuid();
        var row = new CompositeKeyEntity { TenantId = tenant, OrderId = 42, Name = "x" };

        var keyProps = model.Key
            .Select(k => (typeof(CompositeKeyEntity).GetProperty(k.PropertyName)!, k.ClrType))
            .ToList();

        var token = FerretEngine.EncodeAnchor(row, sortFieldProps: [], keyProps, fingerprint: "fp");

        var payload = CursorToken.Decode(token);
        payload.PrimaryKeys.Should().HaveCount(2);
        payload.PrimaryKeys[0].Should().Be(tenant.ToString("N"));
        payload.PrimaryKeys[1].Should().Be("42");
        payload.Fingerprint.Should().Be("fp");
    }

    [Fact]
    public void EncodeAnchor_round_trips_through_BuildCursorPredicate_for_composite_key()
    {
        var model = Model<CompositeKeyEntity>();
        var meta = Meta<CompositeKeyEntity>();
        var tenant = Guid.NewGuid();
        var row = new CompositeKeyEntity { TenantId = tenant, OrderId = 7, Name = "x" };

        var keyProps = model.Key
            .Select(k => (typeof(CompositeKeyEntity).GetProperty(k.PropertyName)!, k.ClrType))
            .ToList();

        var token = FerretEngine.EncodeAnchor(row, sortFieldProps: [], keyProps, fingerprint: "fp");
        var payload = CursorToken.Decode(token);

        var sort = PagedSqlBuilder.EnsureTiebreaker(meta, []);
        var predicate = PagedSqlBuilder.BuildCursorPredicate(
            meta, sort, payload, CursorDirection.Forward, parameterIndex: 0);

        predicate.Parameters.Should().HaveCount(2);
        predicate.Parameters[0].Should().Be(tenant);
        predicate.Parameters[1].Should().Be(7L);
    }

    [Fact]
    public void BuildHydrationSql_single_key_uses_any_fast_path()
    {
        var meta = Meta<SingleKeyEntity>();

        var sql = FerretEngine.BuildHydrationSql(meta);

        sql.Should().Be("SELECT * FROM \"single_key_entities\" WHERE \"id\" = ANY({0})");
    }

    [Fact]
    public void BuildHydrationSql_composite_key_matches_all_columns_via_unnest_tuple()
    {
        var meta = Meta<CompositeKeyEntity>();

        var sql = FerretEngine.BuildHydrationSql(meta);

        sql.Should().Contain("(\"tenant_id\", \"order_id\")");
        sql.Should().Contain("unnest({0}, {1})");
        sql.Should().Contain("FROM \"composite_key_entities\"");
    }
}
