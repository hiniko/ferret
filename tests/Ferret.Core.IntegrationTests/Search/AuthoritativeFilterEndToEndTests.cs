using System.Data.Common;
using Dapper;
using Ferret.Abstractions;
using Ferret.Core.IntegrationTests.Fixtures;
using Ferret.Hydration.Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Ferret.Core.IntegrationTests.Search;

/// <summary>
/// Proves the authoritative-filter guarantee through the real engine against real Postgres, not
/// against a reimplementation of the merge rule. The scenario is tenancy: a caller supplies a
/// filter naming another tenant, and must still only ever see their own rows.
/// </summary>
[Collection("postgres")]
public class AuthoritativeFilterEndToEndTests
{
    private readonly PostgresFixture _fx;

    public AuthoritativeFilterEndToEndTests(PostgresFixture fx) => _fx = fx;

    [SearchableEntity(Table = "authz_docs")]
    public sealed class AuthzDoc : ISearchableEntity<Guid>
    {
        public Guid Id { get; init; }
        [Searchable] public string Body { get; init; } = "";
        [Filterable] public Guid TenantId { get; init; }
        [Filterable] public string Status { get; init; } = "";
    }

    private static readonly Guid Server = Guid.Parse("aaaa0000-0000-0000-0000-000000000001");
    private static readonly Guid Other = Guid.Parse("bbbb0000-0000-0000-0000-000000000002");

    private static readonly Guid Mine1 = Guid.Parse("00000009-0000-0000-0000-000000000001");
    private static readonly Guid Mine2 = Guid.Parse("00000009-0000-0000-0000-000000000002");
    private static readonly Guid Theirs = Guid.Parse("00000009-0000-0000-0000-000000000003");

    [Fact]
    public async Task Caller_filter_cannot_reach_another_tenants_rows()
    {
        await SeedAsync();
        var (engine, session) = await EngineAsync();

        // The caller asks for the OTHER tenant's rows. The server enforces its own.
        var result = await engine.SearchOffsetAsync<AuthzDoc, Guid>(session, new PagedQuery<AuthzDoc, Guid>
        {
            Mode = PaginationMode.Offset,
            Search = "report",
            Limit = 50,
            RequestTotalCount = true,
            Filter = [Filters.Eq(nameof(AuthzDoc.TenantId), Other)],
            AuthoritativeFilter = [Filters.Eq(nameof(AuthzDoc.TenantId), Server)],
        });

        result.Items.Should().OnlyContain(d => d.TenantId == Server);
        result.Items.Select(d => d.Id).Should().BeEquivalentTo([Mine1, Mine2]);
        result.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task Caller_filters_on_other_fields_still_narrow_the_result()
    {
        await SeedAsync();
        var (engine, session) = await EngineAsync();

        var result = await engine.SearchOffsetAsync<AuthzDoc, Guid>(session, new PagedQuery<AuthzDoc, Guid>
        {
            Mode = PaginationMode.Offset,
            Search = "report",
            Limit = 50,
            RequestTotalCount = true,
            Filter = [Filters.Eq(nameof(AuthzDoc.Status), "open")],
            AuthoritativeFilter = [Filters.Eq(nameof(AuthzDoc.TenantId), Server)],
        });

        result.Items.Select(d => d.Id).Should().BeEquivalentTo([Mine1]);
        result.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task Enforcement_holds_without_any_caller_filter()
    {
        await SeedAsync();
        var (engine, session) = await EngineAsync();

        var result = await engine.SearchOffsetAsync<AuthzDoc, Guid>(session, new PagedQuery<AuthzDoc, Guid>
        {
            Mode = PaginationMode.Offset,
            Search = "report",
            Limit = 50,
            RequestTotalCount = true,
            AuthoritativeFilter = [Filters.Eq(nameof(AuthzDoc.TenantId), Server)],
        });

        result.Items.Should().OnlyContain(d => d.TenantId == Server);
        result.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task Enforcement_holds_in_cursor_mode()
    {
        await SeedAsync();
        var (engine, session) = await EngineAsync();

        var result = await engine.SearchCursorAsync<AuthzDoc, Guid>(session, new PagedQuery<AuthzDoc, Guid>
        {
            Mode = PaginationMode.Cursor,
            Search = "report",
            Limit = 50,
            Filter = [Filters.Eq(nameof(AuthzDoc.TenantId), Other)],
            AuthoritativeFilter = [Filters.Eq(nameof(AuthzDoc.TenantId), Server)],
        });

        result.Items.Should().OnlyContain(d => d.TenantId == Server);
        result.Items.Should().HaveCount(2);
    }

    private async Task SeedAsync()
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS authz_docs (
                id uuid PRIMARY KEY,
                body text NOT NULL,
                tenant_id uuid NOT NULL,
                status text NOT NULL
            );
            """);
        await conn.ExecuteAsync("TRUNCATE authz_docs");
        await conn.ExecuteAsync(
            "INSERT INTO authz_docs (id, body, tenant_id, status) VALUES (@Id, @Body, @TenantId, @Status)",
            new[]
            {
                new { Id = Mine1,  Body = "quarterly report",  TenantId = Server, Status = "open" },
                new { Id = Mine2,  Body = "annual report",     TenantId = Server, Status = "closed" },
                new { Id = Theirs, Body = "secret report",     TenantId = Other,  Status = "open" },
            });
    }

    private async Task<(IFerretEngine Engine, DapperSession Session)> EngineAsync()
    {
        var sc = new ServiceCollection();
        sc.AddLogging();
        sc.AddFerret(opts => opts
            .ScanAssembly(typeof(AuthzDoc).Assembly)
            .UseTrigramSearch());
        var sp = sc.BuildServiceProvider();
        var dialect = sp.GetRequiredService<ISqlDialect>();
        var session = new DapperSession(
            ct => Task.FromResult<DbConnection>(new NpgsqlConnection(_fx.ConnectionString)),
            dialect);
        await Task.CompletedTask;
        return (sp.GetRequiredService<IFerretEngine>(), session);
    }
}
