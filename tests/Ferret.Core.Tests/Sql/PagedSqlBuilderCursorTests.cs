using Ferret.Abstractions;
using FluentAssertions;
using Xunit;

namespace Ferret.Core.Tests.Sql;

public class PagedSqlBuilderCursorTests
{
    [SearchableEntity]
    private sealed class Widget : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }
        [Filterable, Sortable]
        public string Name { get; init; } = "";
    }

    private static EntityMetadata Build()
    {
        var reg = EntityRegistry.Build([typeof(Widget)], new SnakeCaseNamingStrategy());
        return EntityMetadata.From(reg.Get<Widget>(), new PostgresDialect());
    }

    [Fact]
    public void Forward_cursor_predicate_compiles_to_tuple_gt()
    {
        var meta = Build();
        var sort = new List<SortClause>
        {
            new() { Field = "Name", Direction = SortDirection.Ascending },
            new() { Field = "Id", Direction = SortDirection.Ascending },
        };
        var cursor = new CursorPayload
        {
            SortKeys = ["Brass Hammer"],
            PrimaryKeys = [Guid.NewGuid().ToString("N")],
            Fingerprint = "fp",
        };

        var fragment = PagedSqlBuilder.BuildCursorPredicate(
            meta, sort, cursor, CursorDirection.Forward, parameterIndex: 0);

        fragment.Sql.Should().Contain("(\"name\", \"id\") > (");
        fragment.Sql.Should().NotContain("<");
        fragment.Parameters[^1].Should().BeOfType<Guid>();
    }

    [Fact]
    public void Backward_cursor_predicate_compiles_to_tuple_lt()
    {
        var meta = Build();
        var sort = new List<SortClause>
        {
            new() { Field = "Name", Direction = SortDirection.Ascending },
            new() { Field = "Id", Direction = SortDirection.Ascending },
        };
        var cursor = new CursorPayload
        {
            SortKeys = ["Brass Hammer"],
            PrimaryKeys = [Guid.NewGuid().ToString("N")],
            Fingerprint = "fp",
        };

        var fragment = PagedSqlBuilder.BuildCursorPredicate(
            meta, sort, cursor, CursorDirection.Backward, parameterIndex: 0);

        fragment.Sql.Should().Contain("(\"name\", \"id\") < (");
        fragment.Parameters[^1].Should().BeOfType<Guid>();
    }

    [Fact]
    public void Tiebreaker_pk_appended_when_user_sort_lacks_pk()
    {
        var meta = Build();
        var sort = new List<SortClause>
        {
            new() { Field = "Name", Direction = SortDirection.Ascending },
        };

        var result = PagedSqlBuilder.EnsureTiebreaker(meta, sort);

        result.Should().HaveCount(2);
        result[^1].Field.Should().Be("Id");
        result[^1].Direction.Should().Be(SortDirection.Ascending);
    }

    [Fact]
    public void Tiebreaker_pk_not_duplicated_when_already_present()
    {
        var meta = Build();
        var sort = new List<SortClause>
        {
            new() { Field = "Name", Direction = SortDirection.Ascending },
            new() { Field = "Id", Direction = SortDirection.Descending },
        };

        var result = PagedSqlBuilder.EnsureTiebreaker(meta, sort);

        result.Should().HaveCount(2);
        result[^1].Field.Should().Be("Id");
        result[^1].Direction.Should().Be(SortDirection.Descending);
    }

    private static EntityMetadata BuildComposite(int keyParts)
    {
        var parts = new[]
        {
            new KeyPart { PropertyName = "TenantId", ColumnName = "tenant_id", ClrType = typeof(Guid) },
            new KeyPart { PropertyName = "Region", ColumnName = "region", ClrType = typeof(int) },
            new KeyPart { PropertyName = "Sku", ColumnName = "sku", ClrType = typeof(string) },
        };
        var key = parts.Take(keyParts).ToArray();
        return new EntityMetadata
        {
            TableName = "items",
            Key = key,
            QuotedTable = "\"items\"",
            ColumnByPropertyName = new Dictionary<string, string>
            {
                ["TenantId"] = "tenant_id",
                ["Region"] = "region",
                ["Sku"] = "sku",
                ["Name"] = "name",
            },
            ClrTypeByPropertyName = new Dictionary<string, Type>
            {
                ["TenantId"] = typeof(Guid),
                ["Region"] = typeof(int),
                ["Sku"] = typeof(string),
                ["Name"] = typeof(string),
            },
            Filterable = new Dictionary<string, FilterableAttribute>(),
            Sortable = new HashSet<string> { "TenantId", "Region", "Sku", "Name" },
            Dialect = new PostgresDialect(),
        };
    }

    [Fact]
    public void CursorPredicate_two_part_key_uses_both_pks()
    {
        var meta = BuildComposite(2);
        var sort = new List<SortClause>
        {
            new() { Field = "Name", Direction = SortDirection.Ascending },
            new() { Field = "TenantId", Direction = SortDirection.Ascending },
            new() { Field = "Region", Direction = SortDirection.Ascending },
        };
        var tenant = Guid.NewGuid();
        var cursor = new CursorPayload
        {
            SortKeys = ["Brass Hammer"],
            PrimaryKeys = [tenant.ToString("N"), "42"],
            Fingerprint = "fp",
        };

        var fragment = PagedSqlBuilder.BuildCursorPredicate(
            meta, sort, cursor, CursorDirection.Forward, parameterIndex: 0);

        fragment.Sql.Should().Contain("(\"name\", \"tenant_id\", \"region\") > (");
        fragment.Parameters.Should().HaveCount(3);
        fragment.Parameters[0].Should().Be("Brass Hammer");
        fragment.Parameters[1].Should().Be(tenant);
        fragment.Parameters[2].Should().Be(42);
    }

    [Fact]
    public void CursorPredicate_three_part_key()
    {
        var meta = BuildComposite(3);
        var sort = new List<SortClause>
        {
            new() { Field = "Name", Direction = SortDirection.Ascending },
            new() { Field = "TenantId", Direction = SortDirection.Ascending },
            new() { Field = "Region", Direction = SortDirection.Ascending },
            new() { Field = "Sku", Direction = SortDirection.Ascending },
        };
        var tenant = Guid.NewGuid();
        var cursor = new CursorPayload
        {
            SortKeys = ["Brass Hammer"],
            PrimaryKeys = [tenant.ToString("N"), "7", "ABC-123"],
            Fingerprint = "fp",
        };

        var fragment = PagedSqlBuilder.BuildCursorPredicate(
            meta, sort, cursor, CursorDirection.Forward, parameterIndex: 0);

        fragment.Sql.Should().Contain("(\"name\", \"tenant_id\", \"region\", \"sku\") > (");
        fragment.Parameters.Should().HaveCount(4);
        fragment.Parameters[1].Should().Be(tenant);
        fragment.Parameters[2].Should().Be(7);
        fragment.Parameters[3].Should().Be("ABC-123");
    }

    [Fact]
    public void CursorPredicate_count_mismatch_throws()
    {
        var meta = BuildComposite(2);
        var sort = new List<SortClause>
        {
            new() { Field = "Name", Direction = SortDirection.Ascending },
            new() { Field = "TenantId", Direction = SortDirection.Ascending },
            new() { Field = "Region", Direction = SortDirection.Ascending },
        };
        var cursor = new CursorPayload
        {
            SortKeys = ["Brass Hammer"],
            PrimaryKeys = [Guid.NewGuid().ToString("N")],
            Fingerprint = "fp",
        };

        Action act = () => PagedSqlBuilder.BuildCursorPredicate(
            meta, sort, cursor, CursorDirection.Forward, parameterIndex: 0);

        act.Should().Throw<InvalidOperationException>();
    }
}
