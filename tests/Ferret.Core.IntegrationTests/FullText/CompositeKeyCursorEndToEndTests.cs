using System.Data.Common;
using Ferret.Abstractions;
using Ferret.Abstractions.Attributes;
using Ferret.Core.Engine;
using Ferret.Core.IntegrationTests.Fixtures;
using Ferret.Hydration.Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Ferret.Core.IntegrationTests.FullText;

[Collection("postgres")]
public class CompositeKeyCursorEndToEndTests
{
    private readonly PostgresFixture _fx;

    public CompositeKeyCursorEndToEndTests(PostgresFixture fx) => _fx = fx;

    [SearchableEntity(Table = "tenant_docs", KeyProperties = new[] { "TenantId", "Id" })]
    public sealed class TenantDoc
    {
        public Guid TenantId { get; init; }
        public long Id { get; init; }

        [Sortable]
        public string Title { get; init; } = "";
    }

    [Fact]
    public async Task Composite_key_cursor_pagination_visits_every_row_once_in_stable_order()
    {
        var sp = BuildServices();
        var engine = sp.GetRequiredService<IFerretEngine>();
        var dialect = sp.GetRequiredService<ISqlDialect>();

        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();

        var tenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var tenantB = Guid.Parse("22222222-2222-2222-2222-222222222222");

        await Exec(conn, """
            DROP TABLE IF EXISTS tenant_docs CASCADE;
            CREATE TABLE tenant_docs (
                tenant_id uuid NOT NULL,
                id bigint NOT NULL,
                title text NOT NULL,
                PRIMARY KEY (tenant_id, id)
            );
            """);

        // 40 rows split across two tenants, same Title so the composite key is the
        // only tiebreaker and ordering must rely on (tenant_id, id).
        var expected = new List<(Guid Tenant, long Id)>();
        for (long i = 1; i <= 20; i++)
        {
            await Exec(conn, $"INSERT INTO tenant_docs (tenant_id, id, title) VALUES ('{tenantA}', {i}, 'same');");
            await Exec(conn, $"INSERT INTO tenant_docs (tenant_id, id, title) VALUES ('{tenantB}', {i}, 'same');");
        }
        // Postgres ASC order over (tenant_id, id): all tenantA rows then all tenantB rows.
        foreach (var t in new[] { tenantA, tenantB })
            for (long i = 1; i <= 20; i++)
                expected.Add((t, i));

        await using var session = NewSession(dialect);

        var seen = new List<(Guid Tenant, long Id)>();
        string? cursor = null;
        var direction = CursorDirection.None;
        const int pageSize = 7;
        var pages = 0;

        while (true)
        {
            var result = await engine.SearchCursorAsync<TenantDoc, object[]>(session, new PagedQuery<TenantDoc, object[]>
            {
                Mode = PaginationMode.Cursor,
                Limit = pageSize,
                Sort = [new SortClause { Field = "Title", Direction = SortDirection.Ascending }],
                Cursor = cursor,
                CursorDirection = direction,
            });

            seen.AddRange(result.Items.Select(d => (d.TenantId, d.Id)));
            pages++;

            if (!result.HasMore) break;
            cursor = result.NextCursor;
            direction = CursorDirection.Forward;
            pages.Should().BeLessThan(100, "pagination must terminate");
        }

        pages.Should().BeGreaterThan(1, "the dataset must span multiple pages");
        seen.Should().HaveCount(expected.Count);
        seen.Should().OnlyHaveUniqueItems("no row may appear twice across pages");
        seen.Should().Equal(expected, "rows must be visited in stable (tenant_id, id) order with no gaps");
    }

    private DapperSession NewSession(ISqlDialect dialect) =>
        new(ct => Task.FromResult<DbConnection>(new NpgsqlConnection(_fx.ConnectionString)), dialect);

    private ServiceProvider BuildServices()
    {
        var sc = new ServiceCollection();
        sc.AddLogging();
        sc.AddFerret(opts => opts
            .ScanAssembly(typeof(TenantDoc).Assembly)
            .UseFullTextSearch(ft => ft.DefaultConfig = "simple")
            .UseDapperHydration());
        return sc.BuildServiceProvider();
    }

    private static async Task Exec(NpgsqlConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}
