using System.Data.Common;
using Ferret.Abstractions;
using Ferret.Abstractions.Attributes;
using Ferret.Abstractions.Models;
using Ferret.Abstractions.Search;
using Ferret.Core.Backends.FullText;
using Ferret.Core.Engine;
using Ferret.Core.IntegrationTests.Fixtures;
using Ferret.Hydration.Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Ferret.Core.IntegrationTests.FullText;

[Collection("postgres")]
public class CompositeKeySearchEndToEndTests
{
    private readonly PostgresFixture _fx;

    public CompositeKeySearchEndToEndTests(PostgresFixture fx) => _fx = fx;

    [SearchableEntity(Table = "ck_docs", KeyProperties = new[] { "TenantId", "Id" })]
    [SearchGroup("content", FullTextConfig = "simple")]
    public sealed class CkDoc
    {
        public Guid TenantId { get; init; }
        public long Id { get; init; }

        [Searchable(Backend = SearchBackend.FullText, Group = "content", Weight = 2.0f)]
        public string Title { get; init; } = "";

        [Filterable]
        public string Region { get; init; } = "";
    }

    [Fact]
    public async Task Composite_key_search_with_filter_uses_multi_column_join_and_returns_correct_rows()
    {
        var sp = BuildServices();
        var registry = sp.GetRequiredService<EntityRegistry>();
        var engine = sp.GetRequiredService<IFerretEngine>();
        var dialect = sp.GetRequiredService<ISqlDialect>();
        var ftOptions = sp.GetRequiredService<FullTextOptions>();
        var model = registry.Get<CkDoc>();

        var tenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var tenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();

        await Exec(conn, $"""
            DROP TABLE IF EXISTS ck_docs_search CASCADE;
            DROP TABLE IF EXISTS ck_docs CASCADE;
            CREATE TABLE ck_docs (
                tenant_id uuid NOT NULL,
                id bigint NOT NULL,
                title text NOT NULL,
                region text NOT NULL,
                PRIMARY KEY (tenant_id, id)
            );
            INSERT INTO ck_docs (tenant_id, id, title, region) VALUES
                ('{tenantA}', 1, 'quantum widget', 'emea'),
                ('{tenantA}', 2, 'quantum gadget', 'apac'),
                ('{tenantB}', 1, 'quantum sprocket', 'emea');
            """);

        await EmitFerretDdlAsync(conn, model, ftOptions);

        await using var session = NewSession(dialect);

        // No filter: full-text "quantum" matches all three composite-key rows.
        var all = await Search(engine, session, "quantum", region: null);
        all.Should().BeEquivalentTo(new[]
        {
            (tenantA, 1L), (tenantA, 2L), (tenantB, 1L),
        });

        // With a Filter: the candidate set flows as N parallel arrays and is joined
        // multi-column against the sidecar. Only emea rows survive.
        var emea = await Search(engine, session, "quantum", region: "emea");
        emea.Should().BeEquivalentTo(new[]
        {
            (tenantA, 1L), (tenantB, 1L),
        });
    }

    private static async Task<IReadOnlyList<(Guid, long)>> Search(
        IFerretEngine engine, DapperSession session, string term, string? region)
    {
        var filter = region is null
            ? Array.Empty<FilterClause>()
            : new[] { new FilterClause { Field = "Region", Operator = FilterOperator.Equals, Value = region } };

        var result = await engine.SearchOffsetAsync<CkDoc, object[]>(session, new PagedQuery<CkDoc, object[]>
        {
            Mode = PaginationMode.Offset,
            Search = term,
            Filter = filter,
            Limit = 50,
        });
        return result.Items.Select(d => (d.TenantId, d.Id)).ToList();
    }

    private static async Task EmitFerretDdlAsync(NpgsqlConnection conn, EntityModel model, FullTextOptions ftOptions)
    {
        var sidecar = FullTextSidecarNaming.TableName(model.TableName, ftOptions);
        var keyColumns = model.Key.Select(k => k.ColumnName).ToList();
        var keyParts = model.Key.Select(k => (k.ColumnName, k.ClrType == typeof(Guid) ? "uuid" : "bigint")).ToList();

        await Exec(conn, FullTextDdlBuilder.CreateSidecarTable(
            sidecar, ftOptions.SidecarSchema, model.TableName, model.Schema, keyParts));

        foreach (var group in model.FullTextGroups)
        {
            var col = FullTextSidecarNaming.ColumnName(group.Name, ftOptions);
            await Exec(conn, FullTextDdlBuilder.AddGroupColumn(sidecar, ftOptions.SidecarSchema, col));
            await Exec(conn, FullTextDdlBuilder.CreateGroupIndex(
                sidecar, ftOptions.SidecarSchema, FullTextSidecarNaming.IndexName(sidecar, col), col));
        }

        await Exec(conn, FullTextDdlBuilder.CreateSyncFunctionAndTrigger(
            sidecar, ftOptions.SidecarSchema, model.TableName, model.Schema, keyColumns,
            FullTextSidecarNaming.SyncFunctionName(model.TableName),
            FullTextSidecarNaming.SyncTriggerName(model.TableName),
            ftOptions.ColumnSuffix, model.FullTextGroups));

        await Exec(conn, FullTextDdlBuilder.Backfill(
            sidecar, ftOptions.SidecarSchema, model.TableName, model.Schema,
            keyColumns, ftOptions.ColumnSuffix, model.FullTextGroups));
    }

    private DapperSession NewSession(ISqlDialect dialect) =>
        new(ct => Task.FromResult<DbConnection>(new NpgsqlConnection(_fx.ConnectionString)), dialect);

    private ServiceProvider BuildServices()
    {
        var sc = new ServiceCollection();
        sc.AddLogging();
        sc.AddFerret(opts => opts
            .ScanAssembly(typeof(CkDoc).Assembly)
            .UsePostgres()
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
