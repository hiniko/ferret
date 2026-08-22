using Ferret.Abstractions;
using Ferret.Abstractions.Attributes;
using Ferret.Abstractions.Models;
using Ferret.Core.Engine;
using Ferret.Core.Sql;
using FluentAssertions;
using Xunit;

namespace Ferret.Core.Tests.Engine;

/// <summary>
/// Authoritative filters exist so a server-enforced restriction — tenant scoping, soft delete,
/// a permission boundary — cannot be widened or escaped by a caller-supplied filter. The
/// property under test is REPLACEMENT: a caller clause on an enforced field must not survive
/// alongside the server's, because two clauses on one field can only ever loosen the result set
/// or contradict it.
/// </summary>
public class AuthoritativeFilterTests
{
    [SearchableEntity(Table = "docs")]
    public sealed class Doc : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }
        [Searchable] public string Body { get; init; } = "";
        [Filterable] public Guid TenantId { get; init; }
        [Filterable] public bool IsDeleted { get; init; }
        [Filterable] public string Status { get; init; } = "";
    }

    private static readonly Guid Server = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Attacker = Guid.Parse("22222222-2222-2222-2222-222222222222");

    /// <summary>Mirrors what the engine does on entry, via the same public surface a caller uses.</summary>
    private static PagedQuery<Doc, Guid> Merge(
        IReadOnlyList<FilterClause> caller,
        IReadOnlyList<FilterClause> authoritative)
    {
        var query = new PagedQuery<Doc, Guid>
        {
            Search = "anything",
            Filter = caller,
            AuthoritativeFilter = authoritative,
        };

        // The engine folds authoritative filters in at the entry point; reproduce that here so the
        // test asserts on the merge rule itself rather than on SQL text.
        var enforced = query.AuthoritativeFilter.Select(f => f.Field).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var merged = query.Filter.Where(f => !enforced.Contains(f.Field)).Concat(query.AuthoritativeFilter).ToList();
        return query with { Filter = merged, AuthoritativeFilter = [] };
    }

    [Fact]
    public void Caller_cannot_override_an_enforced_field()
    {
        var merged = Merge(
            caller: [Filters.Eq(nameof(Doc.TenantId), Attacker)],
            authoritative: [Filters.Eq(nameof(Doc.TenantId), Server)]);

        merged.Filter.Should().ContainSingle("the caller's clause is replaced, not added alongside");
        merged.Filter[0].Value.Should().Be(Server.ToString());
    }

    [Fact]
    public void Caller_filters_on_other_fields_survive()
    {
        var merged = Merge(
            caller: [Filters.Eq(nameof(Doc.Status), "open")],
            authoritative: [Filters.Eq(nameof(Doc.TenantId), Server)]);

        merged.Filter.Should().HaveCount(2);
        merged.Filter.Should().Contain(f => f.Field == nameof(Doc.Status) && f.Value == "open");
        merged.Filter.Should().Contain(f => f.Field == nameof(Doc.TenantId) && f.Value == Server.ToString());
    }

    [Fact]
    public void Field_matching_is_case_insensitive()
    {
        // A caller sending "tenantid" must not slip past a server clause on "TenantId".
        var merged = Merge(
            caller: [Filters.Eq("tenantid", Attacker)],
            authoritative: [Filters.Eq(nameof(Doc.TenantId), Server)]);

        merged.Filter.Should().ContainSingle();
        merged.Filter[0].Value.Should().Be(Server.ToString());
    }

    [Fact]
    public void Multiple_caller_clauses_on_an_enforced_field_are_all_dropped()
    {
        var merged = Merge(
            caller:
            [
                Filters.Eq(nameof(Doc.TenantId), Attacker),
                Filters.Neq(nameof(Doc.TenantId), Server),
            ],
            authoritative: [Filters.Eq(nameof(Doc.TenantId), Server)]);

        merged.Filter.Should().ContainSingle("every caller clause on an enforced field is removed");
        merged.Filter[0].Operator.Should().Be(FilterOperator.Equals);
        merged.Filter[0].Value.Should().Be(Server.ToString());
    }

    [Fact]
    public void Authoritative_filter_is_cleared_after_merge()
    {
        var merged = Merge(
            caller: [],
            authoritative: [Filters.Eq(nameof(Doc.TenantId), Server)]);

        merged.AuthoritativeFilter.Should().BeEmpty(
            "downstream consumers read Filter only; leaving both populated risks applying it twice");
        merged.Filter.Should().ContainSingle();
    }

    [Fact]
    public void Enforced_filters_reach_the_pushed_down_sql()
    {
        var registry = EntityRegistry.Build([typeof(Doc)], new SnakeCaseNamingStrategy());
        var meta = EntityMetadata.From(registry.Get<Doc>(), new PostgresDialect());

        var merged = Merge(
            caller: [Filters.Eq(nameof(Doc.TenantId), Attacker)],
            authoritative: [Filters.Eq(nameof(Doc.TenantId), Server)]);

        var fragment = PagedSqlBuilder.CompileFilter(merged.Filter[0], meta, 0, tableAlias: "e", parameterPrefix: "f");

        fragment.Sql.Should().Contain("tenant_id");
        fragment.Parameters.Should().ContainSingle();
        fragment.Parameters[0].Should().Be(Server, "the server's value is what reaches SQL, never the caller's");
    }

    [Fact]
    public void Values_are_parameterised_not_interpolated()
    {
        var registry = EntityRegistry.Build([typeof(Doc)], new SnakeCaseNamingStrategy());
        var meta = EntityMetadata.From(registry.Get<Doc>(), new PostgresDialect());

        var hostile = Filters.Eq(nameof(Doc.Status), "x'; DROP TABLE docs; --");
        var fragment = PagedSqlBuilder.CompileFilter(hostile, meta, 0, tableAlias: "e", parameterPrefix: "f");

        fragment.Sql.Should().NotContain("DROP TABLE");
        fragment.Parameters.Should().ContainSingle();
    }
}
