using Ferret.Abstractions;
using FluentAssertions;
using Xunit;

namespace Ferret.Core.Tests.Sql;

public class PagedSqlBuilderTests
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
    public void Compile_filter_equals_emits_parameterised_predicate()
    {
        var fragment = PagedSqlBuilder.CompileFilter(
            new FilterClause { Field = "Name", Operator = FilterOperator.Equals, Value = "alpha" },
            Build(), parameterIndex: 0);

        fragment.Sql.Should().Be("\"name\" = @p0");
        fragment.Parameters.Should().ContainSingle().Which.Should().Be("alpha");
    }

    [Fact]
    public void Compile_filter_contains_uses_ilike()
    {
        var fragment = PagedSqlBuilder.CompileFilter(
            new FilterClause { Field = "Name", Operator = FilterOperator.Contains, Value = "frag" },
            Build(), parameterIndex: 3);
        fragment.Sql.Should().Be("\"name\" ILIKE '%' || @p3 || '%'");
    }

    [Theory]
    [InlineData(FilterOperator.NotEquals, "\"name\" <> @p0")]
    [InlineData(FilterOperator.GreaterThanOrEqual, "\"name\" >= @p0")]
    [InlineData(FilterOperator.LessThanOrEqual, "\"name\" <= @p0")]
    public void Compile_filter_comparison_operators_emit_expected_sql(FilterOperator op, string expected)
    {
        var fragment = PagedSqlBuilder.CompileFilter(
            new FilterClause { Field = "Name", Operator = op, Value = "x" },
            Build(), parameterIndex: 0);
        fragment.Sql.Should().Be(expected);
        fragment.Parameters.Should().ContainSingle().Which.Should().Be("x");
    }

    [Fact]
    public void Compile_filter_in_uses_any_with_typed_array()
    {
        var fragment = PagedSqlBuilder.CompileFilter(
            new FilterClause { Field = "Name", Operator = FilterOperator.In, Value = "alpha,beta,gamma" },
            Build(), parameterIndex: 0);

        fragment.Sql.Should().Be("\"name\" = ANY(@p0)");
        fragment.Parameters.Should().ContainSingle();
        fragment.Parameters[0].Should().BeOfType<string[]>()
            .Which.Should().Equal("alpha", "beta", "gamma");
    }

    [Fact]
    public void Compile_filter_in_trims_and_drops_empty_values()
    {
        var fragment = PagedSqlBuilder.CompileFilter(
            new FilterClause { Field = "Name", Operator = FilterOperator.In, Value = " a , ,b " },
            Build(), parameterIndex: 0);
        ((string[])fragment.Parameters[0]!).Should().Equal("a", "b");
    }

    [Fact]
    public void Compile_filter_in_rejects_empty_value()
    {
        Action act = () => PagedSqlBuilder.CompileFilter(
            new FilterClause { Field = "Name", Operator = FilterOperator.In, Value = "" },
            Build(), parameterIndex: 0);
        act.Should().Throw<ArgumentException>().WithMessage("*'in' requires at least one value*");
    }

    [Fact]
    public void Compile_sort_emits_quoted_column_with_direction()
    {
        var fragment = PagedSqlBuilder.CompileSort(
            new SortClause { Field = "Name", Direction = SortDirection.Descending },
            Build());
        fragment.Sql.Should().Be("\"name\" DESC");
    }

    [Fact]
    public void Build_select_assembles_filter_sort_pagination()
    {
        var meta = Build();
        var filter = PagedSqlBuilder.CompileFilter(
            new FilterClause { Field = "Name", Operator = FilterOperator.Equals, Value = "x" },
            meta, 0);
        var sort = PagedSqlBuilder.CompileSort(
            new SortClause { Field = "Name", Direction = SortDirection.Ascending },
            meta);

        var sql = PagedSqlBuilder.BuildSelectIdsAndCount(
            meta, new[] { filter }, new[] { sort }, page: 2, pageSize: 10, candidateIds: null).Sql;

        sql.Should().Contain("SELECT \"id\"");
        sql.Should().Contain("COUNT(*) OVER() AS total_count");
        sql.Should().Contain("WHERE \"name\" = @p0");
        sql.Should().Contain("ORDER BY \"name\" ASC, \"id\" ASC");
        sql.Should().Contain("LIMIT 10 OFFSET 20");
    }

    [Fact]
    public void Build_select_with_candidate_ids_adds_any_predicate()
    {
        var meta = Build();
        var sql = PagedSqlBuilder.BuildSelectIdsAndCount(
            meta, [], [], page: 0, pageSize: 25,
            candidateIds: new object[] { Guid.NewGuid() }).Sql;

        sql.Should().Contain("\"id\" = ANY(@candidate_ids)");
    }

    [Fact]
    public void Compile_filter_rejects_field_not_marked_filterable()
    {
        var unmarked = new FilterClause { Field = "Hidden", Operator = FilterOperator.Equals, Value = "x" };
        Action act = () => PagedSqlBuilder.CompileFilter(unmarked, Build(), 0);
        act.Should().Throw<InvalidOperationException>().WithMessage("*not Filterable*");
    }

    private static EntityMetadata BuildComposite()
    {
        var key = new[]
        {
            new KeyPart { PropertyName = "TenantId", ColumnName = "tenant_id", ClrType = typeof(Guid) },
            new KeyPart { PropertyName = "Sku", ColumnName = "sku", ClrType = typeof(string) },
        };
        return new EntityMetadata
        {
            TableName = "items",
            Key = key,
            QuotedTable = "\"items\"",
            ColumnByPropertyName = new Dictionary<string, string>
            {
                ["TenantId"] = "tenant_id",
                ["Sku"] = "sku",
                ["Name"] = "name",
            },
            ClrTypeByPropertyName = new Dictionary<string, Type>
            {
                ["TenantId"] = typeof(Guid),
                ["Sku"] = typeof(string),
                ["Name"] = typeof(string),
            },
            Filterable = new Dictionary<string, FilterableAttribute>(),
            Sortable = new HashSet<string> { "TenantId", "Sku", "Name" },
            Dialect = new PostgresDialect(),
        };
    }

    [Fact]
    public void EnsureTiebreaker_appends_all_composite_key_columns()
    {
        var meta = BuildComposite();
        var sort = new[]
        {
            new SortClause { Field = "Name", Direction = SortDirection.Descending },
        };

        var result = PagedSqlBuilder.EnsureTiebreaker(meta, sort);

        result.Select(s => s.Field).Should().Equal("Name", "TenantId", "Sku");
        result.Select(s => s.Direction).Should().Equal(
            SortDirection.Descending, SortDirection.Descending, SortDirection.Descending);
    }

    [Fact]
    public void EnsureTiebreaker_skips_key_column_already_trailing()
    {
        var meta = BuildComposite();
        var sort = new[]
        {
            new SortClause { Field = "Name", Direction = SortDirection.Ascending },
            new SortClause { Field = "TenantId", Direction = SortDirection.Ascending },
        };

        var result = PagedSqlBuilder.EnsureTiebreaker(meta, sort);

        result.Select(s => s.Field).Should().Equal("Name", "TenantId", "Sku");
    }

    [Fact]
    public void EnsureTiebreaker_single_key_unchanged()
    {
        var meta = Build();
        var sort = new[]
        {
            new SortClause { Field = "Name", Direction = SortDirection.Descending },
        };

        var result = PagedSqlBuilder.EnsureTiebreaker(meta, sort);

        result.Select(s => s.Field).Should().Equal("Name", "Id");
        result[^1].Direction.Should().Be(SortDirection.Descending);

        var alreadyTrailing = new[] { new SortClause { Field = "Id", Direction = SortDirection.Ascending } };
        PagedSqlBuilder.EnsureTiebreaker(meta, alreadyTrailing).Should().BeSameAs(alreadyTrailing);
    }
}
